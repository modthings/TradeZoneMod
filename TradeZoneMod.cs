using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Ostranauts.Core;
using Ostranauts.ShipGUIs.Trade;
using Ostranauts.Ships;
using System;
using System.Collections.Generic;
using System.Reflection;

[BepInPlugin("com.yourname.tradezones", "Trade Zone Mod", "1.0.3")]
public class TradeZoneMod : BaseUnityPlugin
{
    private readonly Harmony harmony = new Harmony("com.yourname.tradezones");
    internal static ManualLogSource logger;

    void Awake()
    {
        logger = Logger;
        harmony.PatchAll();
        Logger.LogInfo($"[{GetTimestamp()}] Trade Zone Mod initialized");
    }

    private static string GetTimestamp() => $"{DateTime.Now:MMM-dd HH:mm:ss}";

    [HarmonyPatch(typeof(GUITradeBase), "GetZones")]
    static class TradeZonePatch
    {
        private static readonly FieldInfo _mapZonesUserField =
            AccessTools.Field(typeof(GUITradeBase), "_mapZonesUser");

        private static readonly FieldInfo _coUserField =
            AccessTools.Field(typeof(GUITradeBase), "_coUser");

        static void Postfix(GUITradeBase __instance)
        {
            try
            {
                var timestamp = GetTimestamp();
                var mapZonesUser = (Dictionary<JsonZone, Ship>)_mapZonesUserField.GetValue(__instance);
                var coUser = (CondOwner)_coUserField.GetValue(__instance);

                mapZonesUser.Clear();
                var allShips = new List<Ship> { __instance.COSelf.ship };
                allShips.AddRange(__instance.COSelf.ship.GetAllDockedShips());

                logger.LogInfo($"[{timestamp}] Scanning {allShips.Count} ships...");

                int zonesAdded = 0;
                for (int i = 0; i < allShips.Count; i++)
                {
                    var ship = allShips[i];

                    // Handle async loading properly
                    if (ship.LoadState <= Ship.Loaded.Shallow)
                    {
                        Ship loadedShip;
                        if (MonoSingleton<AsyncShipLoader>.Instance.GetShip(ship.strRegID, out loadedShip))
                        {
                            allShips[i] = loadedShip;
                            ship = loadedShip;
                        }
                        else if (MonoSingleton<AsyncShipLoader>.Instance.IsShipLoading(ship.strRegID))
                        {
                            continue;
                        }
                    }

                    var barterZones = ship.GetZones("IsZoneBarter", coUser, false, false);
                    zonesAdded += barterZones.Count;
                    foreach (var zone in barterZones)
                        mapZonesUser[zone] = ship;
                }

                logger.LogInfo($"[{GetTimestamp()}] Added {zonesAdded} barter zones");
            }
            catch (Exception ex)
            {
                logger.LogError($"[{GetTimestamp()}] ERROR: {ex.Message}");
                logger.LogDebug(ex.StackTrace);
            }
        }
    }
}