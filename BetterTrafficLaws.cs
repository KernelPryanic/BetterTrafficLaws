using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using LemonUI.Menus;

namespace BetterTrafficLaws {
	// Resolves the mod's writable data directory.
	//
	// Runtime files (log, config) MUST NOT live anywhere under the game folder. GTA V
	// Enhanced's native ScriptHookV host opens every file present under the game
	// directory at launch — game root AND scripts\ AND its subfolders — with a
	// no-write-share, no-delete, no-rename lock held for the whole session. So any file
	// that already exists at launch can never be rewritten, deleted, or replaced (the
	// long-standing "log won't update / settings don't persist" bug). Proven with
	// handle64 (GTA5_Enhanced.exe owns the handles) + live write-probes: only files that
	// did NOT exist at launch are writable, and even the game root's own ScriptHookV.log
	// is locked. A subfolder under scripts\ does NOT escape this.
	//
	// The fix is to write OUTSIDE the game tree entirely: %APPDATA%\GTA V Mods\...\. The
	// host never scans there, so fixed filenames stay writable every session with normal
	// permissions (verified: an existing file there rewrites/appends fine while the game
	// runs). Init() is kept for API compatibility but the data dir no longer depends on
	// the DLL location.
	public static class ScriptPaths {
		// Group all runtime data under %APPDATA%\GTA V Mods\KernelPryanic\ so this author's
		// GTA V mods share one tree instead of scattering top-level folders.
		const string GameFolderName = "GTA V Mods";
		const string VendorFolderName = "KernelPryanic";
		// This mod's own subfolder under the shared parent.
		const string DataFolderName = "BetterTrafficLaws";

		// The DLL folder, recorded for reference/diagnostics only — NOT used for writes.
		static string directory = AppDomain.CurrentDomain.BaseDirectory ?? ".";

		// The folder the DLL lives in (scripts\). Diagnostic only; not a write target.
		public static string Directory => directory;

		// The mod's writable data folder: %APPDATA%\GTA V Mods\KernelPryanic\BetterTrafficLaws\.
		// All runtime writes (log, config) route through here, outside the game's lock
		// scope. Created on first access.
		public static string DataDirectory {
			get {
				string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
				string path = Path.Combine(appData, GameFolderName, VendorFolderName, DataFolderName);
				if (!System.IO.Directory.Exists(path)) {
					try {
						System.IO.Directory.CreateDirectory(path);
					} catch {
						// If creation fails, callers' own IO guards handle the fallout;
						// never crash path resolution.
					}
				}
				return path;
			}
		}

		// Kept for call-site compatibility (the Script ctor still passes BaseDirectory).
		// Records the DLL folder for diagnostics; the data dir is %APPDATA%, independent
		// of where the DLL was loaded from.
		public static void Init(string baseDirectory) {
			if (!string.IsNullOrEmpty(baseDirectory)) {
				directory = baseDirectory;
			}
		}

		// Resolve a runtime file inside the writable data folder (log/config).
		public static string For(string fileName) => Path.Combine(DataDirectory, fileName);
	}

	// Ordered by severity: a line writes only when its level is at least the threshold.
	public enum LogLevel { Debug, Info, Error }

	public static class Logger {
		// Resolved on each use, not cached: ScriptPaths.Directory is finalized only
		// after the Script constructor calls ScriptPaths.Init, which runs after this
		// type is first touched. Caching here would freeze the pre-Init fallback path.
		static string LogFilePath => ScriptPaths.For("BetterTrafficLaws.log");

		// Lowest level that gets written. Info by default; the ini's [Configuration]
		// LogLevel can drop it to Debug to include the verbose per-violation diagnostics.
		public static LogLevel Threshold { get; set; } = LogLevel.Info;

		public static void ClearLog() {
			try {
				File.WriteAllText(LogFilePath, string.Empty);
			} catch {
				// Logging must never crash the script.
			}
		}

		public static void LogDebug(object message) => Write(LogLevel.Debug, message);
		public static void Log(object message) => Write(LogLevel.Info, message);
		public static void LogError(object message) => Write(LogLevel.Error, message);

		// Always written, ignoring Threshold — for once-per-session triage lines (version,
		// resolved config) that must appear even when the user runs at Error level.
		public static void LogBanner(object message) => Write(LogLevel.Error, message, force: true);

		static void Write(LogLevel level, object message, bool force = false) {
			if (!force && level < Threshold) return;
			try {
				File.AppendAllText(LogFilePath, DateTime.Now + " [" + level.ToString().ToUpperInvariant() + "] " + message + Environment.NewLine);
			} catch {
				// Logging must never crash the script.
			}
		}
	}

	class BetterTrafficLaws : Script {
		readonly ScriptSettings Config;
		readonly string ConfigPath;
		readonly Keys OpenMenu;

		NativeMenu MainMenu;
		NativeCheckboxItem Enabled;
		NativeCheckboxItem EmergencyVehicleExempt;
		NativeListItem<float> CopsDistance;
		NativeListItem<string> SpeedUnits;
		NativeListItem<float> SpeedLimit;
		NativeListItem<float> SpeedLimitHighway;
		NativeListItem<float> SpeedFactor;
		NativeListItem<int> StarsToAdd;
		NativeListItem<LogLevel> LogLevelItem;

		float ConvertedSpeed;
		float SpeedBeforeHit;
		int LastTimeAgainstTraffic;
		int LastTimeDriveOnPavement;
		int LastTimeMobilePhone;
		int LastTimeWheelie;
		int LastTimeBurnout;
		int LastTimeOverspeed;
		int LastTimeOverspeedHighway;
		// A red crossing awaiting its verdict, taken as two heading snapshots. At the crossing the
		// heading and game time are captured; RightTurnGraceMs later a second heading is read and
		// compared. Turned right enough between the two → it was a legal right-on-red, else a ticket.
		// Only the endpoints are sampled, so nothing in between (a momentary flick, a brief stop, a
		// nudge of reverse) can wipe the case. -1 = no pending case.
		int PendingRedCrossTime = -1;
		float PendingRedCrossHeading;
		// Whether a cop witnessed the crossing at the moment it happened. The fine reflects who saw
		// the run at the line, not who happens to be nearby seconds later when the verdict resolves.
		bool PendingRedCrossWitnessed;

		readonly bool RedLightPenaltyEnabled;
		readonly bool OverspeedingPenaltyEnabled;
		readonly bool OverspeedingOnHighwayPenaltyEnabled;
		readonly bool DrivingAgainstTrafficPenaltyEnabled;
		readonly bool DrivingOnPavementPenaltyEnabled;
		readonly bool HitPedPenaltyEnabled;
		readonly bool HitVehiclePenaltyEnabled;
		readonly bool UsingMobilePhonePenaltyEnabled;
		readonly bool DrivingWithoutHelmetPenaltyEnabled;
		readonly bool WheelingPenaltyEnabled;
		readonly bool BurningOutPenaltyEnabled;

		int Period;

		// Last early-return reason logged, to edge-trigger the Debug trace: a stable
		// state (parked, on foot) logs once instead of flooding the log every tick.
		string LastBailReason;

		public BetterTrafficLaws() {
			// Records the DLL folder for diagnostics; runtime writes go to %APPDATA%,
			// independent of this. Must run before any logging or config IO.
			ScriptPaths.Init(BaseDirectory);

			Logger.ClearLog();
			// Banner so the build + data dir land in the log even at Error level (triage).
			Logger.LogBanner("BetterTrafficLaws " + MenuVersion() + " started. Files dir: " + ScriptPaths.DataDirectory);
			MainMenuInit();

			ConfigPath = ScriptPaths.For("BetterTrafficLaws.ini");
			Config = ScriptSettings.Load(ConfigPath);

			// Define default values
			SetConfigValueIfNotDefined("Configuration", "Enabled", "True");
			SetConfigValueIfNotDefined("Configuration", "EmergencyVehicleExempt", "True");
			SetConfigValueIfNotDefined("Configuration", "RedLightPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "OverspeedingPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "OverspeedingOnHighwayPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "DrivingAgainstTrafficPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "DrivingOnPavementPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "HitPedPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "HitVehiclePenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "UsingMobilePhonePenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "DrivingWithoutHelmetPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "WheelingPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "BurningOutPenaltyEnabled", "True");
			SetConfigValueIfNotDefined("Configuration", "CopsVisibilityDistance", 75f);
			SetConfigValueIfNotDefined("Configuration", "StarsToAdd", 1);
			SetConfigValueIfNotDefined("Configuration", "SpeedLimit", 60f);
			SetConfigValueIfNotDefined("Configuration", "SpeedLimitHighway", 120f);
			SetConfigValueIfNotDefined("Configuration", "SpeedFactor", 1f);
			SetConfigValueIfNotDefined("Configuration", "SpeedUnits", "KPH");
			SetConfigValueIfNotDefined("Configuration", "MenuKey", Keys.Shift | Keys.B);
			SetConfigValueIfNotDefined("Configuration", "LogLevel", nameof(LogLevel.Info));

			Enabled.Checked = Config.GetValue("Configuration", "Enabled", true) == true;
			EmergencyVehicleExempt.Checked = Config.GetValue("Configuration", "EmergencyVehicleExempt", true) == true;
			RedLightPenaltyEnabled = Config.GetValue("Configuration", "RedLightPenaltyEnabled", true) == true;
			OverspeedingPenaltyEnabled = Config.GetValue("Configuration", "OverspeedingPenaltyEnabled", true) == true;
			OverspeedingOnHighwayPenaltyEnabled = Config.GetValue("Configuration", "OverspeedingOnHighwayPenaltyEnabled", true) == true;
			DrivingAgainstTrafficPenaltyEnabled = Config.GetValue("Configuration", "DrivingAgainstTrafficPenaltyEnabled", true) == true;
			DrivingOnPavementPenaltyEnabled = Config.GetValue("Configuration", "DrivingOnPavementPenaltyEnabled", true) == true;
			HitPedPenaltyEnabled = Config.GetValue("Configuration", "HitPedPenaltyEnabled", true) == true;
			HitVehiclePenaltyEnabled = Config.GetValue("Configuration", "HitVehiclePenaltyEnabled", true) == true;
			UsingMobilePhonePenaltyEnabled = Config.GetValue("Configuration", "UsingMobilePhonePenaltyEnabled", true) == true;
			DrivingWithoutHelmetPenaltyEnabled = Config.GetValue("Configuration", "DrivingWithoutHelmetPenaltyEnabled", true) == true;
			WheelingPenaltyEnabled = Config.GetValue("Configuration", "WheelingPenaltyEnabled", true) == true;
			BurningOutPenaltyEnabled = Config.GetValue("Configuration", "BurningOutPenaltyEnabled", true) == true;

			CopsDistance.SelectedIndex = CopsDistance.Items.FindIndex(x => x == Config.GetValue("Configuration", "CopsVisibilityDistance", 75f));
			StarsToAdd.SelectedIndex = StarsToAdd.Items.FindIndex(x => x == Config.GetValue("Configuration", "StarsToAdd", 1));
			SpeedLimit.SelectedIndex = SpeedLimit.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedLimit", 60f));
			SpeedLimitHighway.SelectedIndex = SpeedLimitHighway.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedLimitHighway", 120f));
			SpeedFactor.SelectedIndex = SpeedFactor.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedFactor", 1f));
			SpeedUnits.SelectedIndex = SpeedUnits.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedUnits", "KPH"));
			OpenMenu = Config.GetValue("Configuration", "MenuKey", Keys.Shift | Keys.B);

			LogLevel logLevel = ParseLogLevel(Config.GetValue("Configuration", "LogLevel", nameof(LogLevel.Info)));
			LogLevelItem.SelectedIndex = LogLevelItem.Items.FindIndex(x => x == logLevel);
			Logger.Threshold = logLevel;

			// One triage line with the resolved config, written even at Error level.
			Logger.LogBanner($"Config: enabled={Enabled.Checked} emergencyExempt={EmergencyVehicleExempt.Checked} units={SpeedUnits.SelectedItem} limit={SpeedLimit.SelectedItem} highway={SpeedLimitHighway.SelectedItem} factor={SpeedFactor.SelectedItem} stars={StarsToAdd.SelectedItem} menuKey={OpenMenu} logLevel={Logger.Threshold}.");

			Tick += OnTick;
			KeyUp += OnKeyUp;
		}

		static LogLevel ParseLogLevel(string value) =>
			Enum.TryParse(value, true, out LogLevel level) ? level : LogLevel.Info;

		private void SetConfigValueIfNotDefined<T>(string section, string key, T defaultValue) {
			// Detect key *presence*, not value: comparing against default(T) wrongly
			// treats a user's "False"/0 as absent and resets it every launch (so
			// e.g. Enabled=False could never persist). ScriptSettings has no
			// ContainsKey, so probe with two distinct string sentinels — if the key
			// exists both reads return its value; if not, each returns its sentinel.
			if (Config.GetValue(section, key, "\0__a") != Config.GetValue(section, key, "\0__b")) {
				Config.SetValue(section, key, defaultValue);
				Config.Save();
			}
		}

		void OnTick(object sender, EventArgs eventArgs) {
			if (MainMenu.Visible) {
				MainMenu.Process();
			}

			// Throttle the checks to every 6th tick — the nearby-entity scans and
			// raycasts below are too expensive to run every frame. Note the
			// violation predicates measure Game.GameTime deltas, so their windows
			// are sampled at this coarser rate, not per-frame.
			if (Period < 5) {
				Period++;
				return;
			} else {
				Period = 0;
			}

			Vehicle currentVehicle;
			try {
				// Each early return names its reason (Debug, edge-triggered) so a silent
				// "nothing happens" report can be split into "we bailed because X" vs a
				// real detection miss.
				if (!Enabled.Checked) { Bail("disabled"); return; }
				if (Game.Player == null) { Bail("no player"); return; }
				if (Game.Player.Wanted.WantedLevel >= StarsToAdd.SelectedItem) { Bail("already wanted"); return; }
				if (!Game.Player.Character.IsInVehicle()) { Bail("on foot"); return; }
				currentVehicle = Game.Player.Character.CurrentVehicle;
				if (!currentVehicle.GetPedOnSeat(VehicleSeat.Driver).Equals(Game.Player.Character)) { Bail("not the driver"); return; }
				if (currentVehicle.Model.IsBicycle || currentVehicle.Model.IsBoat || currentVehicle.Model.IsHelicopter ||
						currentVehicle.Model.IsPlane || currentVehicle.Model.IsTrain) { Bail("vehicle class exempt"); return; }
				// On duty in an emergency vehicle (police/ambulance/fire) with the siren
				// running, traffic violations are expected — suppress all detection.
				if (EmergencyVehicleExempt.Checked && currentVehicle.ClassType == VehicleClass.Emergency && currentVehicle.IsSirenActive) { Bail("emergency vehicle on duty"); return; }
				ConvertedSpeed = SpeedUnits.SelectedItem == "KPH" ? ToKPH(currentVehicle.Speed) : ToMPH(currentVehicle.Speed);
			} catch (Exception e) {
				// e.ToString() — not e.StackTrace, which is null for a freshly
				// thrown exception and would NRE us right here inside the catch.
				Logger.LogError(e);
				return;
			}

			// Active so the gates above only log their reason on a fresh transition.
			LastBailReason = null;

			// Red light tickets on its own deferred schedule (see ResolveRedLightCrossing); the
			// witness was already captured at the crossing, so apply straight away.
			if (ResolveRedLightCrossing(currentVehicle)) {
				ApplyWantedLevel();
				return;
			}

			List<string> violations = new List<string>();
			if (IsDrivingAgainstTraffic()) violations.Add("against traffic");
			if (IsDrivingOnPavement()) violations.Add("on pavement");
			if (HitPed()) violations.Add("hit ped");
			if (HitVehicle()) violations.Add("hit vehicle");
			if (IsUsingMobilePhone()) violations.Add("mobile phone");
			if (IsDrivingWithoutHelmet(currentVehicle)) violations.Add("no helmet");
			if (IsWheeling(currentVehicle)) violations.Add("wheelie");
			if (IsBurningOut(currentVehicle)) violations.Add("burnout");
			if (IsOverspeeding()) violations.Add("overspeed");
			if (violations.Count == 0) return;

			Logger.LogDebug($"Violation(s) detected: {string.Join(", ", violations)} at {ConvertedSpeed:F0} {SpeedUnits.SelectedItem}. Scanning for cops within {CopsDistance.SelectedItem}.");
			if (WitnessingCop(currentVehicle) != null) ApplyWantedLevel();
		}

		// The nearest allowed cop facing the player (60° cone) with line of sight, or null if none.
		// Highway cops only count while the highway-overspeed predicate holds.
		Ped WitnessingCop(Vehicle vehicle) {
			List<PedHash> allowedCops = new List<PedHash> {
				PedHash.Cop01SFY,
				PedHash.Cop01SMY,
				PedHash.Snowcop01SMM,
				PedHash.Sheriff01SFY,
				PedHash.Sheriff01SMY,
				PedHash.Ranger01SFY,
				PedHash.Ranger01SMY
			};
			if (IsOverspeedingOnHighway()) {
				allowedCops.Add(PedHash.Hwaycop01SMY);
			}
			Ped[] nearbyPeds = World.GetNearbyPeds(Game.Player.Character.Position, CopsDistance.SelectedItem);
			int cops = 0, facing = 0;
			foreach (Ped p in nearbyPeds) {
				try {
					if (!allowedCops.Contains((PedHash)p.Model.Hash) || p.IsDead) continue;
					cops++;

					Vector3 directionToPlayer = (Game.Player.Character.Position - p.Position).Normalized;
					if (Vector3.Dot(directionToPlayer, p.ForwardVector) < Math.Cos(DegreesToAngle(60f))) continue;
					facing++;

					// Native LOS (vs a feet-to-feet raycast) ignores the cop's own body and car — a cop
					// in a cruiser used to fail because the ray hit its own vehicle first.
					if (p.HasClearLineOfSightTo(Game.Player.Character) || p.HasClearLineOfSightTo(vehicle)) {
						return p;
					}
				} catch (Exception e) {
					// A ped can despawn mid-scan; skip it rather than throwing out of OnTick.
					Logger.LogDebug("Skipped a ped during cop scan: " + e.Message);
					continue;
				}
			}
			Logger.LogDebug($"No ticket: {cops} cop(s) in range, {facing} facing, none with line of sight.");
			return null;
		}

		// ApplyWantedLevelChangeNow makes the change take effect this frame, not after the game's
		// internal delay. false = singleplayer.
		void ApplyWantedLevel() {
			Game.Player.Wanted.SetWantedLevel(StarsToAdd.SelectedItem, false);
			Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
			Logger.LogDebug($"Ticketed -> wanted {StarsToAdd.SelectedItem}.");
		}

		// Edge-triggered Debug trace of an early return: logs only when the reason
		// changes, so a sustained state (parked, on foot) writes one line, not one
		// per tick. Costs nothing above the field compare at Info level.
		void Bail(string reason) {
			if (LastBailReason == reason) return;
			LastBailReason = reason;
			Logger.LogDebug("Idle: " + reason + ".");
		}

		const float RightTurnHeadingDelta = 35f; // min rightward turn (deg) that excuses a red crossing
		const int RightTurnGraceMs = 4000;       // wait this long after crossing before judging the turn
		const float QueueClearBuffer = 3.5f;     // metres past the front stopped car that count as crossed

		// A red crossing is judged from two heading snapshots: one at the line, one RightTurnGraceMs
		// later. Sampling only the endpoints — never in between — is what makes it un-gameable: a flick,
		// a brief stop, or a nudge of reverse leaves no trace, only the net turn does. The witnessing
		// cop is captured at the crossing, so the fine reflects who saw the run, not who is near at the
		// verdict. Returns true on the one frame a ticket should land.
		bool ResolveRedLightCrossing(Vehicle vehicle) {
			if (!RedLightPenaltyEnabled) return false;
			try {
				if (PendingRedCrossTime >= 0) {
					if (Game.GameTime - PendingRedCrossTime < RightTurnGraceMs) return false;
					PendingRedCrossTime = -1;
					// Right rotation reads negative; a real turn ends well past the threshold.
					float turned = ((vehicle.Heading - PendingRedCrossHeading + 540f) % 360f) - 180f;
					bool ticket = turned > -RightTurnHeadingDelta && PendingRedCrossWitnessed;
					Logger.LogDebug($"Red-light verdict: turned={turned:F0} witnessed={PendingRedCrossWitnessed} -> {(ticket ? "TICKET" : (turned <= -RightTurnHeadingDelta ? "legal right turn" : "ran but unwitnessed"))}.");
					return ticket;
				}

				// Match co-directional stopped cars by ForwardVector, not Heading(): Heading blends
				// Velocity, which normalizes to garbage at rest and drops the stopped witnesses we need.
				Vector3 myForward = vehicle.ForwardVector;
				List<Vehicle> stopped = new List<Vehicle>(World.GetNearbyVehicles(vehicle.Position, 20f))
					.FindAll(v => v.Driver != null && v.Driver.Exists() && v.Driver != Game.Player.Character && v.Speed == 0)
					.FindAll(v => Vector3.Dot(myForward, v.ForwardVector) >= Math.Cos(DegreesToAngle(30f)));

				float frontFwd = float.NegativeInfinity;
				foreach (Vehicle v in stopped) {
					frontFwd = Math.Max(frontFwd, Vector3.Dot(myForward, v.Position - vehicle.Position));
				}
				int atLights = stopped.FindAll(v => v.IsStoppedAtTrafficLights).Count;

				// First frame past the line with cars held at the red and moving: latch the crossing.
				if (frontFwd <= -QueueClearBuffer && atLights > 0 && ConvertedSpeed > 5) {
					PendingRedCrossTime = Game.GameTime;
					PendingRedCrossHeading = vehicle.Heading;
					PendingRedCrossWitnessed = WitnessingCop(vehicle) != null;
					Logger.LogDebug($"Red-light crossing latched; witnessed={PendingRedCrossWitnessed}.");
				}
				return false;
			} catch (Exception e) {
				// A neighbouring vehicle can despawn mid-evaluation; log and treat as no violation.
				Logger.LogDebug("Red-light check skipped: " + e.Message);
				return false;
			}
		}

		bool IsOverspeeding() {
			if (!OverspeedingPenaltyEnabled) return false;
			if (ConvertedSpeed <= SpeedLimit.SelectedItem)
				LastTimeOverspeed = Game.GameTime;
			return Game.GameTime - LastTimeOverspeed > 2000;
		}

		bool IsOverspeedingOnHighway() {
			if (!OverspeedingOnHighwayPenaltyEnabled) return false;
			if (ConvertedSpeed <= SpeedLimitHighway.SelectedItem)
				LastTimeOverspeedHighway = Game.GameTime;
			return Game.GameTime - LastTimeOverspeedHighway > 2000;
		}

		bool IsDrivingAgainstTraffic() {
			if (!DrivingAgainstTrafficPenaltyEnabled) return false;
			if (Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_AGAINST_TRAFFIC, Game.Player) != 0)
				LastTimeAgainstTraffic = Game.GameTime;
			return Game.GameTime - LastTimeAgainstTraffic > 2000;
		}

		bool IsDrivingOnPavement() {
			if (!DrivingOnPavementPenaltyEnabled) return false;
			if (Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_ON_PAVEMENT, Game.Player) != 0)
				LastTimeDriveOnPavement = Game.GameTime;
			return Game.GameTime - LastTimeDriveOnPavement > 2000;
		}

		bool HitPed() {
			if (!HitPedPenaltyEnabled) return false;
			int timeSinceHitPed = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_PED, Game.Player);
			return !(timeSinceHitPed < 0 || timeSinceHitPed > 500);
		}

		bool HitVehicle() {
			if (!HitVehiclePenaltyEnabled) return false;
			int timeSinceHitVehicle = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_VEHICLE, Game.Player);
			// SpeedBeforeHit holds the speed from the previous check (it's only
			// refreshed when no hit is detected), so a hit is judged by how fast we
			// were going just before impact — this ignores gentle taps at low speed.
			bool hit = !(timeSinceHitVehicle < 0 || timeSinceHitVehicle > 500 || Math.Abs(SpeedBeforeHit) <= 10);
			if (!hit)
				SpeedBeforeHit = ConvertedSpeed;
			return hit;
		}

		bool IsUsingMobilePhone() {
			if (!UsingMobilePhonePenaltyEnabled) return false;
			if (ConvertedSpeed == 0 || !Function.Call<bool>(Hash.IS_PED_RUNNING_MOBILE_PHONE_TASK, Game.Player.Character))
				LastTimeMobilePhone = Game.GameTime;
			return Game.GameTime - LastTimeMobilePhone > 3000;
		}

		bool IsDrivingWithoutHelmet(Vehicle vehicle) {
			if (!DrivingWithoutHelmetPenaltyEnabled) return false;
			if (vehicle.Model.IsBike && ConvertedSpeed > 10 && !Game.Player.Character.IsWearingHelmet) return true;
			return false;
		}

		bool IsWheeling(Vehicle vehicle) {
			if (!WheelingPenaltyEnabled) return false;
			if (!(vehicle.Model.IsBike && !vehicle.IsInAir && !vehicle.IsOnAllWheels))
				LastTimeWheelie = Game.GameTime;
			return Game.GameTime - LastTimeWheelie > 3000;
		}

		bool IsBurningOut(Vehicle vehicle) {
			if (!BurningOutPenaltyEnabled) return false;
			if (!Function.Call<bool>(Hash.IS_VEHICLE_IN_BURNOUT, vehicle))
				LastTimeBurnout = Game.GameTime;
			return Game.GameTime - LastTimeBurnout > 3000;
		}

		double DegreesToAngle(float degrees) {
			return Math.PI * degrees / 180f;
		}

		public float ToMPH(float speed) {
			return speed * 2.23694f * SpeedFactor.SelectedItem;
		}

		public float ToKPH(float speed) {
			return speed * 3.6f * SpeedFactor.SelectedItem;
		}

		void OnKeyUp(object sender, KeyEventArgs e) {
			// Compare KeyData (key + active modifier flags) so a combo like Shift+B matches
			// as configured; for a plain key it equals the key alone.
			if (e.KeyData == OpenMenu) {
				MainMenu.Visible = !MainMenu.Visible;
			}
		}

		void OnCheckboxChange(object sender, EventArgs e) {
			if (sender == Enabled) {
				Config.SetValue("Configuration", "Enabled", Enabled.Checked);
			}
			if (sender == EmergencyVehicleExempt) {
				Config.SetValue("Configuration", "EmergencyVehicleExempt", EmergencyVehicleExempt.Checked);
			}

			Config.Save();
		}

		void OnListChange(object sender, ItemChangedEventArgs<float> e) {
			if (sender == CopsDistance) {
				Config.SetValue("Configuration", "CopsVisibilityDistance", CopsDistance.Items[e.Index]);
			}
			if (sender == SpeedLimit) {
				Config.SetValue("Configuration", "SpeedLimit", SpeedLimit.Items[e.Index]);
			}
			if (sender == SpeedLimitHighway) {
				Config.SetValue("Configuration", "SpeedLimitHighway", SpeedLimitHighway.Items[e.Index]);
			}
			if (sender == SpeedFactor) {
				Config.SetValue("Configuration", "SpeedFactor", SpeedFactor.Items[e.Index]);
			}

			Config.Save();
		}

		void OnListChange(object sender, ItemChangedEventArgs<string> e) {
			if (sender == SpeedUnits) {
				Config.SetValue("Configuration", "SpeedUnits", SpeedUnits.Items[e.Index]);
			}

			Config.Save();
		}

		void OnListChange(object sender, ItemChangedEventArgs<int> e) {
			if (sender == StarsToAdd) {
				Config.SetValue("Configuration", "StarsToAdd", StarsToAdd.Items[e.Index]);
			}

			Config.Save();
		}

		void OnListChange(object sender, ItemChangedEventArgs<LogLevel> e) {
			if (sender == LogLevelItem) {
				Logger.Threshold = LogLevelItem.Items[e.Index];
				Config.SetValue("Configuration", "LogLevel", LogLevelItem.Items[e.Index].ToString());
			}

			Config.Save();
		}

		// The menu subtitle's version comes from the assembly (Properties\AssemblyInfo.cs),
		// which the release workflow stamps from the git tag — so a release only needs the
		// tag, with no hardcoded string to keep in sync. ToString(3) trims the 4-part .NET
		// version (e.g. 3.0.4.0) to semver (3.0.4).
		static string MenuVersion() {
			return "Version " + Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
		}

		void MainMenuInit() {
			MainMenu = new NativeMenu("Better Traffic Laws", MenuVersion());

			Enabled = new NativeCheckboxItem("Enabled");
			MainMenu.Add(Enabled);

			EmergencyVehicleExempt = new NativeCheckboxItem("Emergency Vehicle Exempt") {
				Description = "Suppress detection while driving a police/ambulance/fire vehicle with the siren on."
			};
			MainMenu.Add(EmergencyVehicleExempt);

			CopsDistance = new NativeListItem<float>(
				"Cops Visibility Distance",
				20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f, 105f, 110f, 115f, 120f
			);
			MainMenu.Add(CopsDistance);

			StarsToAdd = new NativeListItem<int>("Stars To Add", 1, 2, 3, 4, 5);
			MainMenu.Add(StarsToAdd);

			SpeedLimit = new NativeListItem<float>(
				"Speed Limit",
				10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f, 150f, 160f, 170f, 180f, 190f, 200f
			);
			MainMenu.Add(SpeedLimit);

			SpeedLimitHighway = new NativeListItem<float>(
				"Speed Limit Highway",
				10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f, 150f, 160f, 170f, 180f, 190f, 200f
			);
			MainMenu.Add(SpeedLimitHighway);

			SpeedFactor = new NativeListItem<float>(
				"Speed Factor",
				0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2f
			);
			MainMenu.Add(SpeedFactor);

			SpeedUnits = new NativeListItem<string>("Speed Units", "", "KPH", "MPH");
			MainMenu.Add(SpeedUnits);

			// Info is the normal level; Debug adds per-violation diagnostics for bug
			// reports; Error quiets everything but failures (banners still write).
			LogLevelItem = new NativeListItem<LogLevel>("Log Level", LogLevel.Info, LogLevel.Debug, LogLevel.Error) {
				Description = "Verbosity of BetterTrafficLaws.log. Debug logs each violation."
			};
			MainMenu.Add(LogLevelItem);

			Enabled.CheckboxChanged += OnCheckboxChange;
			EmergencyVehicleExempt.CheckboxChanged += OnCheckboxChange;
			CopsDistance.ItemChanged += OnListChange;
			StarsToAdd.ItemChanged += OnListChange;
			SpeedLimit.ItemChanged += OnListChange;
			SpeedLimitHighway.ItemChanged += OnListChange;
			SpeedFactor.ItemChanged += OnListChange;
			SpeedUnits.ItemChanged += OnListChange;
			LogLevelItem.ItemChanged += OnListChange;
		}
	}
}
