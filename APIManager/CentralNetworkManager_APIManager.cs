using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Proyecto26;
using System.Linq;
using System;
using MultiplayerARPG.MMO;
using LiteNetLibManager;

namespace MultiplayerARPG.MMO
{

    public static class SteamConfig
    {
        internal const uint AppID = 480;        //480 is dev/test appid for SPACEWARS
#if UNITY_STANDALONE && !CLIENT_BUILD
        /// <summary>
        /// Static/Fixed password we set for all accounts internally in MMORPGKIT as with SteamAuth we only need steamID as username.
        /// But MMORPGKit still needs a dummy password.
        /// </summary>
        internal const string steamPass = @"AIzaSyA4sj5mUuvJIQWp1mdxm5Xbf_ffQLLPqIM";
#endif
    }



#if UNITY_STANDALONE && !CLIENT_BUILD
    public partial class CentralNetworkManager
    {
        protected string SteamUserAuthEndpoint = @"https://partner.steam-api.com/ISteamUserAuth";
        protected string SteamWebKey = @"97DEA59865CCBF04CABA1B5DC03275C2";

        /// <summary>
        /// Sends AuthTicket GET call to Steam
        /// </summary>
        /// <param name="username"></param>
        /// <param name="password"></param>
        /// <param name="result"></param>
        public void callSteamLogin(string steamid, string ticket, RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result, RequestHandlerData requestHandler)
        {
            string url = SteamUserAuthEndpoint + "/AuthenticateUserTicket/v1/";
            url = url + "?key=" + SteamWebKey.Trim() + "&appid=" + SteamConfig.AppID.ToString() + "&ticket=" + ticket.Trim();

            var currentRequest = new RequestHelper
            {
                Uri = url,
                Method = "GET",
                ContentType = "application/x-www-form-urlencoded", // Here you can attach a UploadHandler/DownloadHandler too :)
            };

            RestClient.Request(currentRequest).Then(res =>
            {
                Debug.Log("callSteamLogin Response: " + res.Text);
                SteamRes output = JsonUtility.FromJson<SteamRes>(res.Text);
                //Check if passed steamid is same as ticket steamid
                if (steamid != output.response.@params.steamid)
                {
                    //TODO: Ban if steamids donot match
                    //This means the steamid client sent is not same as AuthTicket steamid and there is probably a hack attempt
                    //We can use here to ban this user
                    result.Invoke(AckResponseCode.Error,
                    new ResponseSteamAuthLoginMessage()
                    {
                        response = "Client SteamID and Authenticated SteamID donot match",
                    });
                }
                //Check if user is VAC or publisher banned and disallow action
                else if (output.response.@params.vacbanned || output.response.@params.publisherbanned)
                {
                    result.Invoke(AckResponseCode.Error,
                    new ResponseSteamAuthLoginMessage()
                    {
                        response = "SteamID is VAC or Publisher Banned",
                    });
                }
                else
                {
                    /*
                    result.Invoke(AckResponseCode.Success,
                    new ResponseSteamAuthLoginMessage()
                    {
                        response = res.Text,
                    }); */

                    //Let's try to login the user from server
                    HandleRequestSteamUserLogin(steamid, result, requestHandler);
                }

            }).Catch(err =>
            {
                var error = err as RequestException;
                Debug.Log("callSteamLogin Error: " + err.Message);
                Debug.Log("callSteamLogin ErrorResponse: " + error.Response);
                result.Invoke(AckResponseCode.Error,
                    new ResponseSteamAuthLoginMessage()
                    {
                        response = error.Response,
                    });
            });
        }


    }
#endif
}
