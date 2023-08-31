﻿using HarmonyLib;
using ItemManager;
using ServerSync;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using wackydatabase.SetData;
using wackydatabase.Datas;
using wackydatabase.Read;
using System.Security.Policy;
using wackydatabase.SetData.SetOldData;
using System.Reflection;
using static CharacterAnimEvent;

namespace wackydatabase.Startup
{

    internal class Startup 
    {

        [HarmonyPatch(typeof(FejdStartup), "SetupObjectDB")]
        static class FejdStartupObjectDBSetup
        {
            static void Postfix()
            {
                if (WMRecipeCust.IsDedServer) return; // dedicated servers don't have to load clones // can load  later

                //if (WMRecipeCust.clonedcache.Value)
                
                WMRecipeCust.Dbgl("Checking Cache Folder and Loading Any Item/Mock Clones");
                ReadFiles clones = new ReadFiles();
                clones.GetCacheClonesOnly();
                SetData.Reload Startup = new SetData.Reload();
                Startup.LoadClonedCachedItems();


                if (WMRecipeCust.FirstSessionRun) {
 
                    SetData.Reload Startup2 = new SetData.Reload();
                    Startup2.AddClonedItemstoObjectDB();

                }

                if (!WMRecipeCust.FirstSessionRun)
                {
                    WMRecipeCust.FirstSessionRun = true;
                }
                          
            }

        }

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPriority(Priority.VeryLow)]
        static class ObjectAwake
        {
            static void Postfix()
            {
                if (!WMRecipeCust.modEnabled.Value)
                    return;

                //WMRecipeCust.WLog.LogWarning("objectDB Awake");

                if (WMRecipeCust.dedLoad) // only for dedicatedservers
                    PrepareLoadData();

                if (!WMRecipeCust.FirstSessionRun)
                {
                    return;
                }

                WMRecipeCust.WLog.LogInfo("ObjectDB 2nd Awake Load"); // Only adds Gameobjects to ObjectDB

                if (ZNet.instance != null) // for loading from a game to back to main menu
                {
                    WMRecipeCust.WLog.LogInfo("ZnetActive in ObjectDB Awake ");
                    if (WMRecipeCust.IsDedServer) { } // dedicated servers don't have to load clones // can load later
                    else
                    {
                        SetData.Reload Startup2 = new SetData.Reload();
                        Startup2.AddClonedItemstoObjectDB();
                       // WMRecipeCust.spawnedinWorld = 1;
                    }

                    if (ZNet.instance.IsServer())
                    {
                        // Only Load if Singleplayer or COOP Server -otherwise need to wait for client
                        PrepareLoadData();

                    }

                    if (!ZNet.instance.IsServer() && WMRecipeCust.HasLobbied) // is client now
                    {
                        WMRecipeCust.ForceLogout = true;
                        // Has Lobbied in Past and could try to use this to get around lockout. 
                        // issettoSinglePlayer = true;
                        if (WMRecipeCust.extraSecurity.Value)
                        {
                            WMRecipeCust.ConfigSync.CurrentVersion = "0.0.1"; // kicking player from server
                            WMRecipeCust.WLog.LogWarning("You hosted a COOP game before trying to connect to a server - LOCKOUT - 0.0.1 - Restart Game " + WMRecipeCust.ConfigSync.CurrentVersion);
                        }
                    }
                }
            }
        }


        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        //[HarmonyPriority(Priority.Last)]
        //[HarmonyPriority(Priority.VeryLow)] // might work
        static class ZNetScene_Awake_Patch_LastWackysDatabase
        {
            static void Postfix()
            {
                if (!WMRecipeCust.modEnabled.Value)
                    return;

                WMRecipeCust.WLog.LogInfo("Znet Awake");
                WMRecipeCust.CurrentReload.LoadZDOsForClones();
                

                if (WMRecipeCust.ServerDedLoad.Value && WMRecipeCust.IsDedServer)
                    WMRecipeCust.dedLoad = true;

            }

        }


        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.Start))]
        public static class FejdStartupPatch
        {
            static void Postfix(FejdStartup __instance)
            {
                if (!WMRecipeCust.IsDedServer) // everyone except ded
                {
                    WMRecipeCust.WLog.LogInfo("extra YML read");
                    WMRecipeCust.context.StartCoroutine(WMRecipeCust.readFiles.GetDataFromFiles(false, true));
                }

                if (ZNet.m_onlineBackend == OnlineBackendType.PlayFab)
                {

                    WMRecipeCust.context._harmony.Patch(AccessTools.DeclaredMethod(typeof(ZPlayFabMatchmaking), nameof(ZPlayFabMatchmaking.CreateLobby)),
                        postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FejdStartupPatch),
                            nameof(gamepassServer))));

                }
                else if (ZNet.m_onlineBackend == OnlineBackendType.Steamworks)
                {
                    WMRecipeCust.context._harmony.Patch(AccessTools.DeclaredMethod(typeof(ZSteamMatchmaking), nameof(ZSteamMatchmaking.RegisterServer)),
                        postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(FejdStartupPatch),
                            nameof(steamServer))));
                }

            }

            private static void steamServer()
            {
                WMRecipeCust.WLog.LogWarning("Steam Lobby is active");
                WMRecipeCust.LobbyRegistered = true;
            }

            private static void gamepassServer()
            {
                WMRecipeCust.WLog.LogWarning("Zplay Lobby is active");
                WMRecipeCust.LobbyRegistered = true;
            }

        }

        public static void DestroyStartupItems()
        {
            var delObj = ObjectDB.instance;
            foreach (var citem in WMRecipeCust.ClonedI)
            {
                try
                {
                    var go = DataHelpers.CheckforSpecialObjects(citem);// check for special cases
                    if (go == null)
                        go = delObj.GetItemPrefab(citem); // normal check
                    delObj.m_items.Remove(go);
                    GameObject.Destroy(go);
                }
                catch { WMRecipeCust.Dbgl($"Error Destorying item {citem}"); }

            }
            WMRecipeCust.Dbgl($"Destroyed cloned items used in startup ");
            WMRecipeCust.ClonedI.Clear();
            delObj.UpdateItemHashes();
        }

        public static bool SinglePlayerchecker
        {
            get { return WMRecipeCust.issettoSinglePlayer; }
            set
            {
                WMRecipeCust.issettoSinglePlayer = true;
                return;
            }
        }
        public static bool IsLocalInstance(ZNet znet)
        {
            if (znet.IsServer() && !znet.IsDedicated() && !WMRecipeCust.LobbyRegistered)
            {
                WMRecipeCust.issettoSinglePlayer = true;
                WMRecipeCust.ForceLogout = true;
                if (WMRecipeCust.extraSecurity.Value)
                {
                    WMRecipeCust.ConfigSync.CurrentVersion = "0.0.1"; // kicking player from server
                    WMRecipeCust.WLog.LogWarning("You Will be kicked from Multiplayer Servers! " + WMRecipeCust.ConfigSync.CurrentVersion);
                }
            }

            if (WMRecipeCust.LobbyRegistered)
            {
                WMRecipeCust.ConfigSync.CurrentVersion = WMRecipeCust.ModVersion; // Just in case goes from singleplayer to hosting. 
                WMRecipeCust.HasLobbied = true;

            }


            return WMRecipeCust.issettoSinglePlayer;
        }

        public static IEnumerator CleartoReload()
        {
            if (!WMRecipeCust.ReloadingOkay)
            {
                WMRecipeCust.WLog.LogInfo("Waiting...");
                WMRecipeCust.ReloadingOkay = true; // Only going to wait .2f for other Syncs to fire off and load, makes sure other mods finish their syncing
                yield return new WaitForSeconds(0.2f);
            }
            WMRecipeCust.WLog.LogInfo("Load Sync Data");
            if (WMRecipeCust.FirstSessionRun)
            {
                WMRecipeCust.context.StartCoroutine(WMRecipeCust.CurrentReload.LoadAllRecipeData(true)); //  Sync Reload 
                WMRecipeCust.FirstSessionRun = false; // reset in a destory patch
            }
            else
            {
                WMRecipeCust.context.StartCoroutine(WMRecipeCust.CurrentReload.LoadAllRecipeData(true, true)); //  Sync Reload SLOW
            }
        }


        public static void CleartoReloadOld()
        {

            WMRecipeCust.WLog.LogInfo("Load Sync Data");
            if (WMRecipeCust.FirstSS)
            {
                WMRecipeCust.context.StartCoroutine(WMRecipeCust.CurrentReload.LoadAllRecipeData(true)); //  Sync Reload 
                WMRecipeCust.FirstSS = false; // reset in a destory patch
            }
            else
            {
                WMRecipeCust.context.StartCoroutine(WMRecipeCust.CurrentReload.LoadAllRecipeData(true, true)); //  Sync Reload SLOW
            }
        }


        public static void PrepareLoadData()
        {

            WMRecipeCust.ReloadingOkay = true;
            if (WMRecipeCust.jsonsFound)
            {
                OldReloadSet oldset = new OldReloadSet();

                WMRecipeCust.WLog.LogWarning("Jsons Found, loading jsons for conversion");

                oldset.OldGetJsons();

                WMRecipeCust.WLog.LogWarning("Jsons Loading into Database, Please stand by");
                oldset.OldReload();

                WMRecipeCust.WLog.LogWarning("Jsons being converted, Please stand by");

                WMRecipeCust.startupserver.SaveYMLBasedONJsons(WMRecipeCust.jsonfiles);

                WMRecipeCust.WLog.LogWarning("Jsons found have been moved to wackysDatabase-OldJsons, any left over should be recreated using console commands");

                WMRecipeCust.WLog.LogError("You should Now Exit, but wackyDB will continue anyways, please remove any jsons leftover from wackydatabase");
            }

            SetData.Reload temp = new SetData.Reload();
            WMRecipeCust.CurrentReload = temp;
            WMRecipeCust.context.StartCoroutine(temp.LoadAllRecipeData(true)); // Singleplayer Reload
        }

    }
}
