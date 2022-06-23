using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;

namespace BetterTrafficLaws {
    public static class Logger {
        public static void LogError(object message) {
            File.AppendAllText("BetterTrafficLaws.log", DateTime.Now + " [Error] " + message + Environment.NewLine);
        }
    }
    class BetterTrafficLaws : Script {
        ScriptSettings Config;
        Keys OpenMenu;

        MenuPool MenuPool;
        UIMenu MainMenu;
        UIMenuCheckboxItem Enabled;
        UIMenuListItem CopsDistance;
        UIMenuListItem SpeedUnits;
        UIMenuListItem SpeedLimit;
        UIMenuListItem SpeedLimitHighway;
        UIMenuListItem SpeedFactor;
        UIMenuListItem StarsToAdd;

        int RedLightCount;
        int AgainstTrafficCount;
        int DriveOnPavementCount;
        int OverspeedCount;
        int OverspeedHighwayCount;
        int Period;

        HashSet<int> trafficLights = new HashSet<int> { 1043035044, 862871082, -655644382, -730685616, 656557234, 865627822, 589548997 };

        public BetterTrafficLaws() {
            MainMenuInit();

            Config = ScriptSettings.Load(@"./scripts/BetterTrafficLaws.ini");
            Enabled.Checked = (Config.GetValue("Configuration", "Enabled", "True") == "True");
            CopsDistance.Index = CopsDistance.Items.FindIndex(x => (float) x == Config.GetValue("Configuration", "CopsVisibilityDistance", 75f));
            StarsToAdd.Index = StarsToAdd.Items.FindIndex(x => (int) x == Config.GetValue("Configuration", "StarsToAdd", 1));
            SpeedLimit.Index = SpeedLimit.Items.FindIndex(x => (float) x == Config.GetValue("Configuration", "SpeedLimit", 40f));
            SpeedLimitHighway.Index = SpeedLimitHighway.Items.FindIndex(x => (float) x == Config.GetValue("Configuration", "SpeedLimitHighway", 90f));
            SpeedFactor.Index = SpeedFactor.Items.FindIndex(x => (float) x == Config.GetValue("Configuration", "SpeedFactor", 1f));
            SpeedUnits.Index = SpeedUnits.Items.FindIndex(x => (string) x == Config.GetValue("Configuration", "SpeedUnits", "KMH"));
            OpenMenu = Config.GetValue("Configuration", "MenuKey", Keys.B);

            Tick += OnTick;
            KeyUp += OnKeyUp;
        }

        void OnTick(object sender, EventArgs eventArgs) {
            if (MenuPool != null && MenuPool.IsAnyMenuOpen()) {
                MenuPool.ProcessMenus();
            }

            if (Period < 5) {
                Period++;
                return;
            } else {
                Period = 0;
            }

            if (!Enabled.Checked || Game.Player.WantedLevel > 0 || Game.Player == null || !Game.Player.Character.IsInVehicle()) return;

            Vehicle currentVehicle = Game.Player.Character.CurrentVehicle;
            float currentSpeed = ((string) SpeedUnits.Items[SpeedUnits.Index] == "KMH") ? ToKMH(currentVehicle.Speed) : ToMPH(currentVehicle.Speed);
            int timeSinceAgainstTraffic = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_AGAINST_TRAFFIC, Game.Player);
            int timeSinceDroveOnPavement = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_ON_PAVEMENT, Game.Player);
            int timeSinceHitPed = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_PED, Game.Player);
            int timeHSinceHitVehicle = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_VEHICLE, Game.Player);

            if (IsRunningOnRedLight(currentVehicle)) {
                RedLightCount++;
            } else {
                RedLightCount = 0;
            }

            if (timeSinceAgainstTraffic == 0) {
                AgainstTrafficCount++;
            } else {
                AgainstTrafficCount = 0;
            }
            if (timeSinceDroveOnPavement == 0) {
                DriveOnPavementCount++;
            } else {
                DriveOnPavementCount = 0;
            }
            if (currentSpeed > (float) SpeedLimit.Items[SpeedLimit.Index]) {
                OverspeedCount++;
            } else {
                OverspeedCount = 0;
            }
            if (currentSpeed > (float) SpeedLimitHighway.Items[SpeedLimitHighway.Index]) {
                OverspeedHighwayCount++;
            } else {
                OverspeedHighwayCount = 0;
            }

            // GTA.UI.Screen.ShowSubtitle("No Arrest " + RedLightCount.ToString());

            if (AgainstTrafficCount < 15 && DriveOnPavementCount < 15 &&
                OverspeedCount < 15 && OverspeedHighwayCount < 15 &&
                (timeSinceHitPed < 0 || timeSinceHitPed > 200) &&
                (timeHSinceHitVehicle < 0 || timeHSinceHitVehicle > 200 || Math.Abs(currentVehicle.Speed) <= 2) &&
                (RedLightCount == 0)
            ) {
                return;
            }

            // GTA.UI.Screen.ShowSubtitle("Arrest " + RedLightCount.ToString());

            List<PedHash> allowedCops = new List<PedHash> {
                (PedHash) PedHash.Cop01SFY,
                (PedHash) PedHash.Cop01SMY,
                (PedHash) PedHash.Snowcop01SMM,
                (PedHash) PedHash.Sheriff01SFY,
                (PedHash) PedHash.Sheriff01SMY,
                (PedHash) PedHash.Ranger01SFY,
                (PedHash) PedHash.Ranger01SMY
            };
            if (OverspeedHighwayCount > 15) {
                allowedCops.AddRange(new List<PedHash> {
                    (PedHash) PedHash.Hwaycop01SMY
                });
            }
            Ped[] nearbyPeds = World.GetNearbyPeds(Game.Player.Character.Position, (float) CopsDistance.Items[CopsDistance.Index]);
            foreach (Ped p in nearbyPeds) {
                if (!allowedCops.Contains((PedHash) p.Model.Hash)) continue;
                if (Math.Abs(Game.Player.Character.Position.Z - p.Position.Z) < 20f) {
                    Game.Player.WantedLevel = (int) StarsToAdd.Items[StarsToAdd.Index];
                }
            }
        }

        bool IsRunningOnRedLight(Vehicle target) {
            try {
                List<Vehicle> nearbyVehicles = new List<Vehicle>(World.GetNearbyVehicles(Game.Player.Character.Position, 20f))
                    .FindAll(vehicle => vehicle.Driver.Exists() && vehicle.Driver != Game.Player.Character && vehicle.Speed == 0)
                    .FindAll(vehicle => Vector3.Dot(Heading(target), Heading(vehicle)) >= Math.Cos(DegreesToAngle(30f)));
                List<Vehicle> inFrontOnTrafficLight = nearbyVehicles
                    .FindAll(vehicle => Vector3.Dot(Heading(target), (target.Position - vehicle.Position).Normalized) <
                        Math.Cos(DegreesToAngle(Math.Min(45f * target.Position.DistanceTo(vehicle.Position) / 7f, 75f))));
                List<Vehicle> inBackOnTrafficLight = nearbyVehicles
                    .FindAll(vehicle => Vector3.Dot(Heading(target), (target.Position - vehicle.Position).Normalized) > Math.Cos(DegreesToAngle(90f)));

                if (inFrontOnTrafficLight.Count > 0) return false;

                int votes = 0;
                foreach (Vehicle v in inBackOnTrafficLight) {
                    if (v.IsStoppedAtTrafficLights) {
                        votes++;
                    }
                }

                return ((votes > 0.5 * inBackOnTrafficLight.Count) && (target.Speed > 0f));
            } catch {
                return false;
            }
        }

        Prop GetNearestTrafficLight() {
            Prop[] nearbyProps = World.GetNearbyProps(Game.Player.Character.Position, 40f);
            foreach (Prop p in nearbyProps) {
                if (trafficLights.Contains((int) p.Model.Hash)) return p;
            }

            return null;
        }

        Vector3 Heading(Entity entity) {
            return (0.5f * entity.ForwardVector.Normalized + 0.5f * entity.Velocity.Normalized).Normalized;
        }

        double DegreesToAngle(float degrees) {
            return Math.PI * degrees / 180f;
        }

        public float ToMPH(float speed) {
            return speed * 2.23694f * (float) SpeedFactor.Items[SpeedFactor.Index];
        }

        public float ToKMH(float speed) {
            return speed * 3.6f * (float) SpeedFactor.Items[SpeedFactor.Index];
        }

        void OnKeyUp(object sender, KeyEventArgs e) {
            if (e.KeyCode == OpenMenu && !MenuPool.IsAnyMenuOpen()) {
                MainMenu.Visible = !MainMenu.Visible;
            }
        }

        void OnCheckBoxChange(UIMenu sender, UIMenuCheckboxItem checkboxItem, bool Checked) {
            if (checkboxItem == Enabled) {
                string value = Checked ? "True" : "False";
                Config.SetValue("Configuration", "Enabled", value);
            }

            Config.Save();
        }

        void OnListChange(UIMenu sender, UIMenuListItem listItem, int newIndex) {
            if (listItem == CopsDistance) {
                Config.SetValue("Configuration", "CopsVisibilityDistance", (float) CopsDistance.Items[CopsDistance.Index]);
            }
            if (listItem == StarsToAdd) {
                Config.SetValue("Configuration", "StarsToAdd", (int) StarsToAdd.Items[StarsToAdd.Index]);
            }
            if (listItem == SpeedLimit) {
                Config.SetValue("Configuration", "SpeedLimit", (float) SpeedLimit.Items[SpeedLimit.Index]);
            }
            if (listItem == SpeedLimitHighway) {
                Config.SetValue("Configuration", "SpeedLimitHighway", (float) SpeedLimitHighway.Items[SpeedLimitHighway.Index]);
            }
            if (listItem == SpeedFactor) {
                Config.SetValue("Configuration", "SpeedFactor", (float) SpeedFactor.Items[SpeedFactor.Index]);
            }
            if (listItem == SpeedUnits) {
                Config.SetValue("Configuration", "SpeedUnits", (string) SpeedUnits.Items[SpeedUnits.Index]);
            }

            Config.Save();
        }

        void MainMenuInit() {
            MenuPool = new MenuPool();
            MainMenu = new UIMenu("Better Traffic Laws", "Version 1.3.0");
            MenuPool.Add(MainMenu);

            Enabled = new UIMenuCheckboxItem("Enabled", true);
            MainMenu.AddItem(Enabled);

            CopsDistance = new UIMenuListItem("Cops visibility distance",
                new List<object> { 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f }, 0);
            MainMenu.AddItem(CopsDistance);

            StarsToAdd = new UIMenuListItem("Stars to add",
                new List<object> { 1, 2, 3, 4, 5 }, 0);
            MainMenu.AddItem(StarsToAdd);

            SpeedLimit = new UIMenuListItem("Speed limit",
                new List<object> { 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f, 105f, 110f, 115f, 120f, 125f, 130f }, 0);
            MainMenu.AddItem(SpeedLimit);

            SpeedLimitHighway = new UIMenuListItem("Speed limit highway",
                new List<object> { 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f, 105f, 110f, 115f, 120f, 125f, 130f }, 0);
            MainMenu.AddItem(SpeedLimitHighway);

            SpeedFactor = new UIMenuListItem("Speed factor",
                new List<object> { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2f }, 0);
            MainMenu.AddItem(SpeedFactor);

            SpeedUnits = new UIMenuListItem("Speed units", new List<object> { "KMH", "MPH" }, 0);
            MainMenu.AddItem(SpeedUnits);

            MainMenu.OnCheckboxChange += OnCheckBoxChange;
            MainMenu.OnListChange += OnListChange;
        }
    }
}