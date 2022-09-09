using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLibManager;
using LiteNetLib.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
using System;
using System.Text;
using Steamworks;

namespace MultiplayerARPG.MMO
{

    public partial class CentralNetworkManager : LiteNetLibManager.LiteNetLibManager
    {
#if UNITY_STANDALONE && !CLIENT_BUILD
        [DevExtMethods("RegisterMessages")]
        protected void DevExtRegisterFirebaseAuthMessages()
        {
            RegisterRequestToServer<RequestUserLoginMessage, ResponseSteamAuthLoginMessage>(MMORequestTypes.RequestSteamLogin, HandleRequestSteamLogin);
            RegisterRequestToServer<RequestUserRegisterMessage, ResponseSteamAuthLoginMessage>(MMORequestTypes.RequestSteamRegister, HandleRequestSteamRegister);
        }
#endif
    }

    public static partial class MMORequestTypes
    {
        public const ushort RequestSteamLogin = 5012;
        public const ushort RequestSteamRegister = 5013;
    }

    /// <summary>
    /// General Response handler for firebase, we pass string or jsonText as response in it
    /// </summary>
    public struct ResponseSteamAuthLoginMessage : INetSerializable
    {
        public string response;
        public UITextKeys message;
        public string userId;
        public string accessToken;
        public long unbanTime;
        public void Deserialize(NetDataReader reader)
        {
            response = reader.GetString();
            message = (UITextKeys)reader.GetPackedUShort();
            userId = reader.GetString();
            accessToken = reader.GetString();
            unbanTime = reader.GetPackedLong();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(response);
            writer.PutPackedUShort((ushort)message);
            writer.Put(userId);
            writer.Put(accessToken);
            writer.PutPackedLong(unbanTime);
        }
    }

    public partial class CentralNetworkManager
    {
        /// <summary>
        /// Custom Name validation to be used in delegate of NameValidating class
        /// Currently using it to disable name validation as we will use email for username
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        protected virtual bool customNameValidation(string name)
        {
            Debug.Log("Using customNameValidation");
            return true;
        }
#if UNITY_STANDALONE && !CLIENT_BUILD
        public bool RequestSteamLogin(string steamid, string ticket, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestSteamLogin, new RequestUserLoginMessage()
            {
                username = steamid,
                password = ticket,
            }, responseDelegate: callback);
        }

        public bool RequestSteamRegister(string email, string password, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            return ClientSendRequest(MMORequestTypes.RequestSteamRegister, new RequestUserRegisterMessage()
            {
                username = email,
                password = password,
                email = email
            }, responseDelegate: callback);
        }

        protected async UniTaskVoid HandleRequestSteamLogin(
            RequestHandlerData requestHandler,
            RequestUserLoginMessage request,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result)
        {
            string message = "";
            string steamid = request.username;
            string ticket = request.password;
            NameValidating.overrideUsernameValidating = customNameValidation;
            //string email = request.email;
            Debug.Log("Pre API call");
            callSteamLogin(steamid, ticket, result);
            Debug.Log("Post API call");           
        }

        protected async UniTaskVoid HandleRequestSteamRegister(
            RequestHandlerData requestHandler,
            RequestUserRegisterMessage request,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result)
        {
            string message = "";
            string email = request.username;
            string password = request.password;
            NameValidating.overrideUsernameValidating = customNameValidation;
            //string email = request.email;
            Debug.Log("Pre API call");
            //callSteamRegister(email, password, result);
            Debug.Log("Post API call");
        }
#endif
    }


    public partial class MMOClientInstance : MonoBehaviour
    {
        public void RequestSteamLogin(string steamid, string ticket, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            centralNetworkManager.RequestSteamLogin(steamid, ticket, callback);
        }
        public void RequestSteamRegister(string email, string password, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            centralNetworkManager.RequestSteamRegister(email, password, callback);
        }

        /// <summary>
        /// Try to initialize SteamClient using pre-configured app id, if game is launched without steam, it will quit and launch from steam.
        /// Will return true if SteamClient intialized or already running, otherwise return false
        /// </summary>
        /// <returns></returns>
        public ErrorDetailsRes trySteamInit()
        {
            ErrorDetailsRes err = new ErrorDetailsRes();
#if !UNITY_EDITOR
            if (Steamworks.SteamClient.RestartAppIfNecessary(SteamConfig.AppID))
            {
                err.error = true;
                err.code = 1;
                err.message = "Game launched outside steam, restarting it from Steam";
                DelayExitGame(3.0f);    //Exit after delay as the game is about to be relaunched from Steam
                return err;
            }
#endif
            if (Steamworks.SteamClient.IsValid)
            {
                err.error = false;
                err.code = 0;
                err.message = "SteamClient Already Initialized";
                return err;
            }
            //Try initialization
            try
            {
                Steamworks.SteamClient.Init(SteamConfig.AppID, true);
                err.error = false;
                err.code = 0;
                err.message = "SteamClient Initialized";
                return err;
            }
            catch (System.Exception e)
            {
                // Something went wrong! Steam is closed?
                ///Common reasons for exceptions are:
                //Steam is closed
                //Can't find steam_api dlls
                //Don't have permission to open appid
                err.error = true;
                err.code = 2;
                err.message = "SteamClient Initialization failed, is Steam running ? - " + e.Message;
                return err;
            }
            err.error = false;
            err.code = 0;
            err.message = "SteamClient Initialized";
            return err;
        }

        /// <summary>
        /// Gets a Steam AuthSessionTicket and encodes it to Hex encoded string
        /// </summary>
        /// <returns></returns>
        public string GetSteamAuthTicket(out AuthTicket ticket)
        {
            ticket = SteamUser.GetAuthSessionTicket();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ticket.Data.Length; i++)
            {
                sb.AppendFormat("{0:x2}", ticket.Data[i]);
            }
            return sb.ToString();
        }

        System.Collections.IEnumerator DelayExitGame(float delay)
        {
            yield return new WaitForSeconds(delay); //wait 5 secconds
            Application.Quit();
        }
    }
}