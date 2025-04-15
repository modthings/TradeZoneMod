using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.Ships;
using System;
using System.Collections.Generic;
using System.Reflection;

[BepInPlugin("com.yourname.tradezones", "Trade Zone Freedom", "1.4.2")]
public class TradeZoneFreedom : BaseUnityPlugin
{
    private readonly Harmony harmony = new Harmony("com.yourname.tradezones.freedom");
    internal static ManualLogSource logger;

    void Awake()
    {
        logger = Logger;
        try
        {
            harmony.PatchAll(typeof(TradeZoneFreedom).Assembly);
            LogWithTimestamp("Trade Zone Freedom mod loaded successfully");
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed to initialize: {ex}");
        }
    }

    private static void LogWithTimestamp(string message, LogLevel level = LogLevel.Info)
    {
        logger.Log(level, $"[{DateTime.Now:MMM-dd HH:mm:ss.fff}] {message}");
    }

    [HarmonyPatch(typeof(GUITradeBase), "GetZones")]
    static class GetZonesPatch
    {
        private static readonly FieldInfo bZonesLoadingField =
            AccessTools.Field(typeof(GUITradeBase), "bZonesLoading");
        private static readonly FieldInfo coUserField =
            AccessTools.Field(typeof(GUITradeBase), "_coUser");
        private static readonly FieldInfo mapZonesUserField =
            AccessTools.Field(typeof(GUITradeBase), "_mapZonesUser");

        static void Postfix(GUITradeBase __instance)
        {
            try
            {
                // Get required fields via reflection
                var mapZonesUser = (Dictionary<JsonZone, Ship>)mapZonesUserField.GetValue(__instance);
                var coUser = (CondOwner)coUserField.GetValue(__instance);
                bool isLoading = (bool)bZonesLoadingField.GetValue(__instance);

                LogWithTimestamp($"Starting zone scan (Loading: {isLoading})", LogLevel.Debug);

                // Get all relevant ships (including station areas)
                var allShips = GetAccessibleShips(__instance.COSelf.ship, coUser);
                LogWithTimestamp($"Found {allShips.Count} accessible ships/areas", LogLevel.Debug);

                int zonesAdded = 0;
                for (int i = 0; i < allShips.Count; i++)
                {
                    var ship = allShips[i];
                    if (ship == null) continue;

                    Ship actualShip = ship;
                    if (ship.LoadState <= Ship.Loaded.Shallow)
                    {
                        Ship loadedShip;
                        if (MonoSingleton<AsyncShipLoader>.Instance.GetShip(ship.strRegID, out loadedShip))
                        {
                            actualShip = loadedShip;
                            allShips[i] = loadedShip; // Update the list
                        }
                        else if (MonoSingleton<AsyncShipLoader>.Instance.IsShipLoading(ship.strRegID))
                        {
                            bZonesLoadingField.SetValue(__instance, true);
                            LogWithTimestamp($"Ship {ship.strRegID} is loading...", LogLevel.Debug);
                            continue;
                        }
                    }

                    var zones = actualShip.GetZones("IsZoneBarter", coUser, false, false);
                    zonesAdded += zones.Count;
                    foreach (var zone in zones)
                    {
                        if (!mapZonesUser.ContainsKey(zone))
                        {
                            mapZonesUser[zone] = actualShip;
                            LogWithTimestamp($"Added zone: {zone.strName} from {actualShip.strRegID}", LogLevel.Debug);
                        }
                    }
                }

                LogWithTimestamp($"Added {zonesAdded} zones from {allShips.Count} ships/areas", LogLevel.Info);
                bZonesLoadingField.SetValue(__instance, isLoading);
            }
            catch (Exception ex)
            {
                LogWithTimestamp($"ERROR: {ex.Message}", LogLevel.Error);
                LogWithTimestamp(ex.StackTrace, LogLevel.Debug);
            }
        }

        private static List<Ship> GetAccessibleShips(Ship playerShip, CondOwner coUser)
        {
            var accessibleShips = new List<Ship>();
            var uniqueShipIds = new HashSet<string>();

            // Add player ship first
            AddShipIfUnique(playerShip, accessibleShips, uniqueShipIds);

            // Get main station reference
            var crewSim = CrewSim.system;
            if (crewSim != null)
            {
                Ship mainStation = crewSim.GetNearestStation(
                    playerShip.objSS.vPosx,
                    playerShip.objSS.vPosy,
                    excludeOutposts: true);

                if (mainStation != null)
                {
                    // Get all station areas (ships with same prefix)
                    var stationAreas = crewSim.GetShipsBySubString(mainStation.strRegID);
                    foreach (var area in stationAreas)
                    {
                        if (area == null || area.bDestroyed) continue;
                        AddShipIfUnique(area, accessibleShips, uniqueShipIds);
                        AddDockedShips(area, accessibleShips, uniqueShipIds);
                    }

                    // Add ships docked to main station
                    AddDockedShips(mainStation, accessibleShips, uniqueShipIds);
                }
            }

            // Add ships docked to player ship
            AddDockedShips(playerShip, accessibleShips, uniqueShipIds);

            return accessibleShips;
        }

        private static void AddDockedShips(Ship ship, List<Ship> shipList, HashSet<string> uniqueIds)
        {
            var dockedShips = ship?.GetAllDockedShipsFull();
            if (dockedShips == null) return;

            foreach (var docked in dockedShips)
            {
                AddShipIfUnique(docked, shipList, uniqueIds);
            }
        }

        private static void AddShipIfUnique(Ship ship, List<Ship> shipList, HashSet<string> uniqueIds)
        {
            if (ship == null || uniqueIds.Contains(ship.strRegID)) return;

            shipList.Add(ship);
            uniqueIds.Add(ship.strRegID);
        }
    }
}