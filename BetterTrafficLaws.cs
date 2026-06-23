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
    // Resolves paths next to the loaded DLL (the scripts/ folder) rather than
    // against the process CWD, which for the game is the root folder where
    // GTA5.exe lives. SHVDN3 loads scripts in place, so Assembly.Location is the
    // real scripts/ path; falling back to the relative name keeps it working if
    // a host ever shadow-copies the assembly to a temp dir.
    public static class ScriptPaths {
        public static readonly string Directory =
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
                ?? AppDomain.CurrentDomain.BaseDirectory;

        public static string For(string fileName) => Path.Combine(Directory, fileName);
    }

    public static class Logger {
        static readonly string LogFilePath = ScriptPaths.For("BetterTrafficLaws.log");

        public static void ClearLog() {
            try {
                if (File.Exists(LogFilePath)) {
                    File.WriteAllText(LogFilePath, string.Empty);
                }
            } catch {
                // Logging must never crash the script; a locked/unwritable log is
                // not worth taking the mod down for.
            }
        }

        public static void LogError(object message) {
            try {
                File.AppendAllText(LogFilePath, DateTime.Now + " [Error] " + message + Environment.NewLine);
            } catch {
                // See ClearLog: swallow file-IO failures by design.
            }
        }
    }

    class BetterTrafficLaws : Script {
        readonly ScriptSettings Config;
        readonly string ConfigPath;
        readonly Keys OpenMenu;

        NativeMenu MainMenu;
        NativeCheckboxItem Enabled;
        NativeListItem<float> CopsDistance;
        NativeListItem<string> SpeedUnits;
        NativeListItem<float> SpeedLimit;
        NativeListItem<float> SpeedLimitHighway;
        NativeListItem<float> SpeedFactor;
        NativeListItem<int> StarsToAdd;

        float ConvertedSpeed;
        float SpeedBeforeHit;
        int LastTimeAgainstTraffic;
        int LastTimeDriveOnPavement;
        int LastTimeMobilePhone;
        int LastTimeWheelie;
        int LastTimeBurnout;
        int LastTimeOverspeed;
        int LastTimeOverspeedHighway;

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

        public BetterTrafficLaws() {
            Logger.ClearLog();
            MainMenuInit();

            ConfigPath = ScriptPaths.For("BetterTrafficLaws.ini");
            Config = ScriptSettings.Load(ConfigPath);

            // Define default values
            SetConfigValueIfNotDefined("Configuration", "Enabled", "True");
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
            SetConfigValueIfNotDefined("Configuration", "MenuKey", Keys.B.ToString());

            Enabled.Checked = Config.GetValue("Configuration", "Enabled", true) == true;
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
            OpenMenu = Config.GetValue("Configuration", "MenuKey", Keys.B);

            Tick += OnTick;
            KeyUp += OnKeyUp;
        }

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
                if (!Enabled.Checked || Game.Player == null || Game.Player.Wanted.WantedLevel >= StarsToAdd.SelectedItem || !Game.Player.Character.IsInVehicle()) return;
                currentVehicle = Game.Player.Character.CurrentVehicle;
                if (!currentVehicle.GetPedOnSeat(VehicleSeat.Driver).Equals(Game.Player.Character) ||
                        currentVehicle.Model.IsBicycle || currentVehicle.Model.IsBoat || currentVehicle.Model.IsHelicopter ||
                            currentVehicle.Model.IsPlane || currentVehicle.Model.IsTrain) return;
                ConvertedSpeed = SpeedUnits.SelectedItem == "KPH" ? ToKPH(currentVehicle.Speed) : ToMPH(currentVehicle.Speed);
            } catch (Exception e) {
                // e.ToString() — not e.StackTrace, which is null for a freshly
                // thrown exception and would NRE us right here inside the catch.
                Logger.LogError(e);
                return;
            }

            if (!IsRunningOnRedLight(currentVehicle) && !IsDrivingAgainstTraffic() && !IsDrivingOnPavement() &&
                !HitPed() && !HitVehicle() &&
                !IsUsingMobilePhone() && !IsDrivingWithoutHelmet(currentVehicle) &&
                !IsWheeling(currentVehicle) && !IsBurningOut(currentVehicle) && !IsOverspeeding())
                return;

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
            foreach (Ped p in nearbyPeds) {
                try {
                    if (!allowedCops.Contains((PedHash)p.Model.Hash) || p.IsDead) continue;

                    // Only count a cop who is facing the player (within a 60° cone)...
                    Vector3 directionToPlayer = (Game.Player.Character.Position - p.Position).Normalized;
                    float dotProduct = Vector3.Dot(directionToPlayer, p.ForwardVector);
                    if (dotProduct < Math.Cos(DegreesToAngle(60f))) continue;

                    // ...and has line of sight to the player or their vehicle.
                    RaycastResult raycast = World.Raycast(p.Position, Game.Player.Character.Position, IntersectFlags.Everything);
                    if (raycast.HitEntity != null && (raycast.HitEntity.Handle == currentVehicle.Handle || raycast.HitEntity.Handle == Game.Player.Character.Handle)) {
                        // SetWantedLevel queues the change; ApplyWantedLevelChangeNow
                        // makes it take effect this frame instead of after the game's
                        // internal delay. false = singleplayer.
                        Game.Player.Wanted.SetWantedLevel(StarsToAdd.SelectedItem, false);
                        Game.Player.Wanted.ApplyWantedLevelChangeNow(false);
                        break; // A visible cop is enough; stop scanning.
                    }
                } catch {
                    // A ped can despawn mid-scan, invalidating its handle; skip it
                    // rather than letting one stale entity throw out of OnTick.
                    continue;
                }
            }
        }

        bool IsRunningOnRedLight(Vehicle vehicle) {
            if (!RedLightPenaltyEnabled) return false;
            try {
                List<Vehicle> nearbyVehicles = new List<Vehicle>(World.GetNearbyVehicles(Game.Player.Character.Position, 20f))
                    .FindAll(v => v.Driver.Exists() && v.Driver != Game.Player.Character && v.Speed == 0)
                    .FindAll(v => Vector3.Dot(Heading(vehicle), Heading(v)) >= Math.Cos(DegreesToAngle(30f)));
                List<Vehicle> inFrontOnTrafficLight = nearbyVehicles
                    .FindAll(v => Vector3.Dot(Heading(vehicle), (vehicle.Position - v.Position).Normalized) <
                        Math.Cos(DegreesToAngle(Math.Min(45f * vehicle.Position.DistanceTo(v.Position) / 7f, 75f))));
                List<Vehicle> inBackOnTrafficLight = nearbyVehicles
                    .FindAll(v => Vector3.Dot(Heading(vehicle), (vehicle.Position - v.Position).Normalized) > Math.Cos(DegreesToAngle(90f)));

                if (inFrontOnTrafficLight.Count > 0) return false;

                int votes = 0;
                foreach (Vehicle v in inBackOnTrafficLight) {
                    if (v.IsStoppedAtTrafficLights) votes++;
                }

                return (votes > 0.5 * inBackOnTrafficLight.Count) && (ConvertedSpeed > 5);
            } catch {
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

        Vector3 Heading(Entity entity) {
            return (0.5f * entity.ForwardVector.Normalized + 0.5f * entity.Velocity.Normalized).Normalized;
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
            if (e.KeyCode == OpenMenu) {
                MainMenu.Visible = !MainMenu.Visible;
            }
        }

        void OnCheckboxChange(object sender, EventArgs e) {
            if (sender == Enabled) {
                Config.SetValue("Configuration", "Enabled", Enabled.Checked);
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

        void MainMenuInit() {
            MainMenu = new NativeMenu("Better Traffic Laws", "Version 3.0.3");

            Enabled = new NativeCheckboxItem("Enabled");
            MainMenu.Add(Enabled);

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

            Enabled.CheckboxChanged += OnCheckboxChange;
            CopsDistance.ItemChanged += OnListChange;
            StarsToAdd.ItemChanged += OnListChange;
            SpeedLimit.ItemChanged += OnListChange;
            SpeedLimitHighway.ItemChanged += OnListChange;
            SpeedFactor.ItemChanged += OnListChange;
            SpeedUnits.ItemChanged += OnListChange;
        }
    }
}
