using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using LemonUI.Menus;

namespace BetterTrafficLaws {
    public static class Logger {
        public static void LogError(object message) {
            File.AppendAllText("BetterTrafficLaws.log", DateTime.Now + " [Error] " + message + Environment.NewLine);
        }
    }

    class BetterTrafficLaws : Script {
        readonly ScriptSettings Config;
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
        int AgainstTrafficCount;
        int DriveOnPavementCount;
        int MobilePhoneCount;
        int WhellieCount;
        int BurnoutCount;
        int OverspeedCount;
        int OverspeedHighwayCount;

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

        readonly HashSet<int> TrafficLights = new HashSet<int> { 1043035044, 862871082, -655644382, -730685616, 656557234, 865627822, 589548997 };

        public BetterTrafficLaws() {
            MainMenuInit();

            Config = ScriptSettings.Load(@"./scripts/BetterTrafficLaws.ini");
            Enabled.Checked = Config.GetValue("Configuration", "Enabled", "True") == "True";

            RedLightPenaltyEnabled = Config.GetValue("Configuration", "RedLightPenaltyEnabled", "True") == "True";
            OverspeedingPenaltyEnabled = Config.GetValue("Configuration", "OverspeedingPenaltyEnabled", "True") == "True";
            OverspeedingOnHighwayPenaltyEnabled = Config.GetValue("Configuration", "OverspeedingOnHighwayPenaltyEnabled", "True") == "True";
            DrivingAgainstTrafficPenaltyEnabled = Config.GetValue("Configuration", "DrivingAgainstTrafficPenaltyEnabled", "True") == "True";
            DrivingOnPavementPenaltyEnabled = Config.GetValue("Configuration", "DrivingOnPavementPenaltyEnabled", "True") == "True";
            HitPedPenaltyEnabled = Config.GetValue("Configuration", "HitPedPenaltyEnabled", "True") == "True";
            HitVehiclePenaltyEnabled = Config.GetValue("Configuration", "HitVehiclePenaltyEnabled", "True") == "True";
            UsingMobilePhonePenaltyEnabled = Config.GetValue("Configuration", "UsingMobilePhonePenaltyEnabled", "True") == "True";
            DrivingWithoutHelmetPenaltyEnabled = Config.GetValue("Configuration", "DrivingWithoutHelmetPenaltyEnabled", "True") == "True";
            WheelingPenaltyEnabled = Config.GetValue("Configuration", "WheelingPenaltyEnabled", "True") == "True";
            BurningOutPenaltyEnabled = Config.GetValue("Configuration", "BurningOutPenaltyEnabled", "True") == "True";

            CopsDistance.SelectedIndex = CopsDistance.Items.FindIndex(x => x == Config.GetValue("Configuration", "CopsVisibilityDistance", 75f));
            StarsToAdd.SelectedIndex = StarsToAdd.Items.FindIndex(x => x == Config.GetValue("Configuration", "StarsToAdd", 1));
            SpeedLimit.SelectedIndex = SpeedLimit.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedLimit", 40f));
            SpeedLimitHighway.SelectedIndex = SpeedLimitHighway.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedLimitHighway", 90f));
            SpeedFactor.SelectedIndex = SpeedFactor.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedFactor", 1f));
            SpeedUnits.SelectedIndex = SpeedUnits.Items.FindIndex(x => x == Config.GetValue("Configuration", "SpeedUnits", "KMH"));
            OpenMenu = Config.GetValue("Configuration", "MenuKey", Keys.B);

            Tick += OnTick;
            KeyUp += OnKeyUp;
        }

        void OnTick(object sender, EventArgs eventArgs) {
            if (MainMenu.Visible) {
                MainMenu.Process();
            }

            if (Period < 5) {
                Period++;
                return;
            } else {
                Period = 0;
            }

            Vehicle currentVehicle;
            try {
                if (!Enabled.Checked || Game.Player == null || Game.Player.WantedLevel > 0 || !Game.Player.Character.IsInVehicle()) return;
                currentVehicle = Game.Player.Character.CurrentVehicle;
                if (!currentVehicle.GetPedOnSeat(VehicleSeat.Driver).Equals(Game.Player.Character) || currentVehicle.Model.IsBicycle) return;
                ConvertedSpeed = SpeedUnits.SelectedItem == "KMH" ? ToKMH(currentVehicle.Speed) : ToMPH(currentVehicle.Speed);
            } catch (Exception e) {
                Logger.LogError(e.StackTrace.ToString());
                return;
            }

            if (!IsRunningOnRedLight(currentVehicle) && !IsDrivingAgainstTraffic() && !IsDrivingOnPavement() &&
                !HitPed() && !HitVehicle(currentVehicle) &&
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
                if (!allowedCops.Contains((PedHash)p.Model.Hash)) continue;
                if (Math.Abs(Game.Player.Character.Position.Z - p.Position.Z) < 20f)
                    Game.Player.WantedLevel = StarsToAdd.SelectedItem;
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

                return (votes > 0.5 * inBackOnTrafficLight.Count) && (vehicle.Speed > 0f);
            } catch {
                return false;
            }
        }

        bool IsOverspeeding() {
            if (!OverspeedingPenaltyEnabled) return false;
            if (ConvertedSpeed > SpeedLimit.SelectedItem)
                OverspeedCount++;
            else
                OverspeedCount = 0;
            if (OverspeedCount > 15) return true;
            return false;
        }

        bool IsOverspeedingOnHighway() {
            if (!OverspeedingOnHighwayPenaltyEnabled) return false;
            if (ConvertedSpeed > SpeedLimitHighway.SelectedItem)
                OverspeedHighwayCount++;
            else
                OverspeedHighwayCount = 0;
            if (OverspeedHighwayCount > 15) return true;
            return false;
        }

        bool IsDrivingAgainstTraffic() {
            if (!DrivingAgainstTrafficPenaltyEnabled) return false;
            if (Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_AGAINST_TRAFFIC, Game.Player) == 0)
                AgainstTrafficCount++;
            else
                AgainstTrafficCount = 0;
            if (AgainstTrafficCount > 15) return true;
            return false;
        }

        bool IsDrivingOnPavement() {
            if (!DrivingOnPavementPenaltyEnabled) return false;
            if (Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_DROVE_ON_PAVEMENT, Game.Player) == 0)
                DriveOnPavementCount++;
            else
                DriveOnPavementCount = 0;
            if (DriveOnPavementCount > 15) return true;
            return false;
        }

        bool HitPed() {
            if (!HitPedPenaltyEnabled) return false;
            int timeSinceHitPed = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_PED, Game.Player);
            return !(timeSinceHitPed < 0 || timeSinceHitPed > 200);
        }

        bool HitVehicle(Vehicle vehicle) {
            if (!HitVehiclePenaltyEnabled) return false;
            int timeSinceHitVehicle = Function.Call<int>(Hash.GET_TIME_SINCE_PLAYER_HIT_VEHICLE, Game.Player);
            return !(timeSinceHitVehicle < 0 || timeSinceHitVehicle > 200 || Math.Abs(vehicle.Speed) <= 2);
        }

        bool IsUsingMobilePhone() {
            if (!UsingMobilePhonePenaltyEnabled) return false;
            if (Function.Call<bool>(Hash.IS_PED_RUNNING_MOBILE_PHONE_TASK, Game.Player.Character))
                MobilePhoneCount++;
            else
                MobilePhoneCount = 0;
            if (MobilePhoneCount > 15) return true;
            return false;
        }

        bool IsDrivingWithoutHelmet(Vehicle vehicle) {
            if (!DrivingWithoutHelmetPenaltyEnabled) return false;
            if (vehicle.Model.IsBike && vehicle.Speed > 2 && !Game.Player.Character.IsWearingHelmet) return true;
            return false;
        }

        bool IsWheeling(Vehicle vehicle) {
            if (!WheelingPenaltyEnabled) return false;
            if (vehicle.Model.IsBike && !vehicle.IsInAir && !vehicle.IsOnAllWheels) {
                WhellieCount++;
                if (WhellieCount > 15) return true;
                return false;
            }
            WhellieCount = 0;
            return false;
        }

        bool IsBurningOut(Vehicle vehicle) {
            if (!BurningOutPenaltyEnabled) return false;
            if (Function.Call<bool>(Hash.IS_VEHICLE_IN_BURNOUT, vehicle))
                BurnoutCount++;
            else
                BurnoutCount = 0;
            if (BurnoutCount > 25) return true;
            return false;
        }

        // Deprecated
        Prop GetNearestTrafficLight() {
            Prop[] nearbyProps = World.GetNearbyProps(Game.Player.Character.Position, 40f);
            foreach (Prop p in nearbyProps) {
                if (TrafficLights.Contains(p.Model.Hash)) return p;
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
            return speed * 2.23694f * SpeedFactor.SelectedItem;
        }

        public float ToKMH(float speed) {
            return speed * 3.6f * SpeedFactor.SelectedItem;
        }

        void OnKeyUp(object sender, KeyEventArgs e) {
            if (e.KeyCode == OpenMenu && !MainMenu.Visible) {
                MainMenu.Visible = !MainMenu.Visible;
            }
        }

        void OnCheckboxChange(object sender, EventArgs e) {
            if (sender == Enabled) {
                string value = Enabled.Checked ? "True" : "False";
                Config.SetValue("Configuration", "Enabled", value);
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
            MainMenu = new NativeMenu("Better Traffic Laws", "Version 2.0.2");

            Enabled = new NativeCheckboxItem("Enabled", true);
            MainMenu.Add(Enabled);

            CopsDistance = new NativeListItem<float>("Cops visibility distance", 20f, 25f, 30f, 35f, 40f, 45f, 50f, 55f, 60f, 65f, 70f, 75f, 80f, 85f, 90f, 95f, 100f);
            MainMenu.Add(CopsDistance);

            StarsToAdd = new NativeListItem<int>("Stars to add", 1, 2, 3, 4, 5);
            MainMenu.Add(StarsToAdd);

            SpeedLimit = new NativeListItem<float>("Speed limit", 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f, 150f, 160f, 170f, 180f, 190f, 200f);
            MainMenu.Add(SpeedLimit);

            SpeedLimitHighway = new NativeListItem<float>("Speed limit highway", 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f, 110f, 120f, 130f, 140f, 150f, 160f, 170f, 180f, 190f, 200f);
            MainMenu.Add(SpeedLimitHighway);

            SpeedFactor = new NativeListItem<float>("Speed factor", 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f, 1f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 1.6f, 1.7f, 1.8f, 1.9f, 2f);
            MainMenu.Add(SpeedFactor);

            SpeedUnits = new NativeListItem<string>("Speed units", "KMH", "MPH");
            MainMenu.Add(SpeedUnits);

            Enabled.Activated += OnCheckboxChange;
            CopsDistance.ItemChanged += OnListChange;
            StarsToAdd.ItemChanged += OnListChange;
            SpeedLimit.ItemChanged += OnListChange;
            SpeedLimitHighway.ItemChanged += OnListChange;
            SpeedFactor.ItemChanged += OnListChange;
            SpeedUnits.ItemChanged += OnListChange;
        }
    }
}
