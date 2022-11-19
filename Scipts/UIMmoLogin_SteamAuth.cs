using UnityEngine.Events;
using UnityEngine.UI;
using LiteNetLibManager;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Steamworks;

namespace MultiplayerARPG.MMO
{
    public partial class UIMmoLogin : UIBase
    {
        /// <summary>
        /// We should cancel this ticket once we are done with Authentication
        /// </summary>
        AuthTicket ticketRef;

        ulong steamid;

        //We initiate SteamLogin as soon as the login screen is loaded, ideally we donot even want to show the login/registration screen
        private void OnEnable()
        {
            trySteamMMOLogin();
        }

        /// <summary>
        /// Performs client->server steam ticket validation before login. 
        /// Should be called as soon as Login screen is loaded, ideally from Awake or even directly without showing login screen,
        /// as we donot need to see login fields.
        /// SteamAuth does not use a username or password, steamID becomes username and password is hardcoded server-side
        /// </summary>
        public void trySteamMMOLogin()
        {
            if (LoggingIn)
                return;

            UISceneGlobal uiSceneGlobal = UISceneGlobal.Singleton;
            //First check if Steam is Init
            ErrorDetailsRes err = MMOClientInstance.Singleton.trySteamInit();
            if (err.error)
            {
                Debug.Log("SteamClientInit: " + err.message);
                uiSceneGlobal.ShowMessageDialog("SteamClient error", err.message);
                return;
            }
            // Clear stored username and password
            PlayerPrefs.SetString(keyUsername, string.Empty);
            PlayerPrefs.Save();

            LoggingIn = true;

            var playername = SteamClient.Name;
            steamid = SteamClient.SteamId.Value;
            Debug.Log("proceedMMOLogin ClientSteamID: " + steamid);
            //PlayerPrefs.SetString(keyUsername, Username);

            //We should show a loading screen between this call and the callback's return
            string ticket = MMOClientInstance.Singleton.GetSteamAuthTicket(out ticketRef);
            Debug.Log("SteamAuthTicket: " + ticket);

            MMOClientInstance.Singleton.RequestSteamLogin(steamid.ToString(), ticket, OnSteamLogin);
        }
        public void OnSteamLogin(ResponseHandlerData responseHandler, AckResponseCode responseCode, ResponseSteamAuthLoginMessage response)
        {
            ticketRef.Cancel();
            LoggingIn = false;
            Debug.Log(responseCode);
            Debug.Log(response.response);
            //If firebaseLogin was not success
            if (responseCode == AckResponseCode.Timeout)
            {
                UISceneGlobal.Singleton.ShowMessageDialog("Timeout Error", "MMO Server did not respond in time");
                return;
            }
            if (responseCode != AckResponseCode.Success)
            {
                //FirebaseErrorRes error = JsonUtility.FromJson<FirebaseErrorRes>(response.response);
                Debug.Log("onSteamLogin Error: " + response.response);
                UISceneGlobal.Singleton.ShowMessageDialog("SteamLogin Error", response.response);
                return;
            }
            //If success, try Kit's login
            //MMOClientInstance.Singleton.RequestUserLogin(Username, Password, OnLoginCustom);

            //Save userid/playerid, usefull for things like steamid
            PlayerPrefs.SetString("_PLAYERID_", response.username);
            Debug.Log("_PLAYERID_ " + response.username);
            //APIManager.instance.updatePlayerId(response.userId);

            if (onLoginSuccess != null)
            {
                onLoginSuccess.Invoke();
            }
        }
    }
}