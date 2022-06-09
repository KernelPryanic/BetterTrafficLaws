using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using NativeUI;

namespace BetterTrafficLaws {
    class BetterTrafficLaws : Script {
        ScriptSettings config;
        Keys openMenu;

        MenuPool menuPool;
        UIMenu mainMenu;
        UIMenuCheckboxItem enabled;
        UIMenuListItem copsDistance;
        UIMenuListItem speedUnits;
        UIMenuListItem speedLimit;
        UIMenuListItem speedLimitHighway;
        UIMenuListItem speedFactor;
        UIMenuListItem starsToAdd;

        int redLightCount;
        int againstTrafficCount;
        int driveOnPavementCount;
        int overspeedCount;
        int overspeedHighwayCount;
        int period;

        HashSet<int> trafficLights = new HashSet<int> { 1043035044, 862871082, -655644382, -730685616, 656557234, 865627822, 589548997 };

        public BetterTrafficLaws() {
            MainMenu();

            config = ScriptSettings.Load(@"./scripts/BetterTrafficLaws.ini");
            enabled.Checked = (config.GetValue("Configuration", "Enabled", "True") == "True");
            copsDistance.Index = copsDistance.Items.FindIndex(x => (float) x == config.GetValue("Configuration", "Cops visibility distance", 75f));
            starsToAdd.Index = starsToAdd.Items.FindIndex(x => (int) x == config.GetValue("Configuration", "Stars to add", 1));
            speedLimit.Index = speedLimit.Items.FindIndex(x => (float) x == config.GetValue("Configuration", "Speed limit", 40f));
            speedLimitHighway.Index = speedLimitHighway.Items.FindIndex(x => (float) x == config.GetValue("Configuration", "Speed limit highway", 90f));
            speedFactor.Index = speedFactor.Items.FindIndex(x => (float) x == config.GetValue("Configuration", "Speed factor", 1f));
            speedUnits.Index = speedUnits.Items.FindIndex(x => (string) x == config.GetValue("Configuration", "Speed units", "KMH"));
            openMenu = config.GetValue("Configuration", "Menu key", Keys.B);

            Interval = 10;
            Tick += OnTick;
            KeyUp += OnKeyUp;
        }

        void OnTick(object sender, EventArgs e) {
            if (menuPool != null && menuPool.IsAnyMenuOpen()) {
                menuPool.ProcessMenus();
            }

            if (period < 4) {
                period++;
                return;
            } else {
                period = 0;
            }

            if (!enabled.Checked || Game.Player.WantedLevel > 0 || !Game.Player.Character.IsInVehicle()) return;

            Vehicle currentVehicle = Game.Player.Character.CurrentVehicle;
            float currentSpeed = ((string) speedUnits.Items[speedUnits.Index] == "KMH") ? ToKMH(currentVehicle.Speed) : ToMPH(currentVehicle.Speed);
            int timeSinceAgainstTraffic = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_AGAINST_TRAFFIC, Game.Player);
            int timeSinceDroveOnPavement = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_ON_PAVEMENT, Game.Player);
            int timeSinceHitPed = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_PED, Game.Player);
            int timeHSinceitVehicle = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_VEHICLE, Game.Player);

            if (IsRunningOnRedLight(currentVehicle)) {
                redLightCount++;
            } else {
                redLightCount = 0;
            }

            if (timeSinceAgainstTraffic == 0) {
                againstTrafficCount++;
            } else {
                againstTrafficCount = 0;
            }
            if (timeSinceDroveOnPavement == 0) {
                driveOnPavementCount++;
            } else {
                driveOnPavementCount = 0;
            }
            if (currentSpeed > (float) speedLimit.Items[speedLimit.Index]) {
                overspeedCount++;
            } else {
                overspeedCount = 0;
            }
            if (currentSpeed > (float) speedLimitHighway.Items[speedLimitHighway.Index]) {
                overspeedHighwayCount++;
            } else {
                overspeedHighwayCount = 0;
            }

            // UI.ShowSubtitle("No Arrest " + redLightCount.ToString());

            if (againstTrafficCount < 16 && driveOnPavementCount < 16 &&
                overspeedCount < 16 && overspeedHighwayCount < 16 &&
                (timeSinceHitPed < 0 || timeSinceHitPed > 200) &&
                (timeHSinceitVehicle < 0 || timeHSinceitVehicle > 200) &&
                (redLightCount == 0)
            ) {
                return;
            }

            // UI.ShowSubtitle("Arrest " + redLightCount.ToString());

            List<PedHash> allowedCops = new List<PedHash> {
                (PedHash) PedHash.Cop01SFY,
                (PedHash) PedHash.Cop01SMY,
                (PedHash) PedHash.Snowcop01SMM,
                (PedHash) PedHash.Sheriff01SFY,
                (PedHash) PedHash.Sheriff01SMY,
                (PedHash) PedHash.Ranger01SFY,
                (PedHash) PedHash.Ranger01SMY
            };
            if (overspeedHighwayCount > 16) {
                allowedCops.AddRange(new List<PedHash> {
                    (PedHash) PedHash.Hwaycop01SMY
                });
            }
            Ped[] nearbyPeds = World.GetNearbyPeds(Game.Player.Character.Position, (float) copsDistance.Items[copsDistance.Index]);
            foreach (Ped p in nearbyPeds) {
                if (!allowedCops.Contains((PedHash) p.Model.Hash)) continue;
                if (Math.Abs(Game.Player.Character.Position.Z - p.Position.Z) < 20f) {
                    Game.Player.WantedLevel = (int) starsToAdd.Items[starsToAdd.Index];
                }
            }
        }

        void OnKeyUp(object sender, KeyEventArgs e) {
            if (e.KeyCode == openMenu && !menuPool.IsAnyMenuOpen()) {
                mainMenu.Visible = !mainMenu.Visible;
            }
        }

        void OnCheckBoxChange(UIMenu sender, UIMenuCheckboxItem checkboxItem, bool Checked) {
            if (checkboxItem == enabled) {
                string value = Checked ? "True" : "False";
                config.SetValue("Configuration", "Enabled", value);
            }

            config.Save();
        }

        void OnListChange(UIMenu sender, UIMenuListItem listItem, int newIndex) {
            if (listItem == copsDistance) {
                config.SetValue("Configuration", "Cops visibility distance", (float) copsDistance.Items[copsDistance.Index]);
            }
            if (listItem == starsToAdd) {
                config.SetValue("Configuration", "Stars to add", (int) starsToAdd.Items[starsToAdd.Index]);
            }
            if (listItem == speedLimit) {
                config.SetValue("Configuration", "Speed limit", (float) speedLimit.Items[speedLimit.Index]);
            }
            if (listItem == speedLimitHighway) {
                config.SetValue("Configuration", "Speed limit highway", (float) speedLimitHighway.Items[speedLimitHighway.Index]);
            }
            if (listItem == speedFactor) {
                config.SetValue("Configuration", "Speed factor", (float) speedFactor.Items[speedFactor.Index]);
            }
            if (listItem == speedUnits) {
                config.SetValue("Configuration", "Speed units", (string) speedUnits.Items[speedUnits.Index]);
            }

            config.Save();
        }

        void MainMenu() {
            menuPool = new MenuPool();
            mainMenu = new UIMenu("Better Traffic Laws", "Version 1.2.0");
            menuPool.Add(mainMenu);

            enabled = new UIMenuCheckboxItem("Enabled", true);
            mainMenu.AddItem(enabled);

            copsDistance = new UIMenuListItem("Cops visibility distance",
                new List<object> { 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f }, 0);
            mainMenu.AddItem(copsDistance);

            starsToAdd = new UIMenuListItem("Stars to add",
                new List<object> { 1, 2, 3, 4, 5 }, 0);
            mainMenu.AddItem(starsToAdd);

            speedLimit = new UIMenuListItem("Speed limit",
                new List<object> { 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f, 105f, 110f, 115f, 120f, 125f, 130f }, 0);
            mainMenu.AddItem(speedLimit);

            speedLimitHighway = new UIMenuListItem("Speed limit highway",
                new List<object> { 10f, 15f, 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f, 105f, 110f, 115f, 120f, 125f, 130f }, 0);
            mainMenu.AddItem(speedLimitHighway);

            speedFactor = new UIMenuListItem("Speed factor",
                new List<object> { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2f }, 0);
            mainMenu.AddItem(speedFactor);

            speedUnits = new UIMenuListItem("Speed units", new List<object> { "KMH", "MPH" }, 0);
            mainMenu.AddItem(speedUnits);

            mainMenu.OnCheckboxChange += OnCheckBoxChange;
            mainMenu.OnListChange += OnListChange;
        }

        bool IsRunningOnRedLight(Vehicle target) {
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
            return speed * 2.23694f * (float) speedFactor.Items[speedFactor.Index];
        }

        public float ToKMH(float speed) {
            return speed * 3.6f * (float) speedFactor.Items[speedFactor.Index];
        }
    }
}