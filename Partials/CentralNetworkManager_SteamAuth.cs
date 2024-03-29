using System.Collections.Generic;
using UnityEngine;
using LiteNetLib;
using LiteNetLibManager;
using LiteNetLib.Utils;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;
using System;
using System.Text;
#if !UNITY_WEBGL
using Steamworks;
#endif
using System.Text.RegularExpressions;

#if !UNITY_WEBGL
namespace MultiplayerARPG.MMO
{

    public partial class CentralNetworkManager : LiteNetLibManager.LiteNetLibManager
    {
        [DevExtMethods("RegisterMessages")]
        protected void DevExtRegisterSteamAuthMessages()
        {
            RegisterRequestToServer<RequestUserLoginMessage, ResponseSteamAuthLoginMessage>(MMORequestTypes.RequestSteamLogin, HandleRequestSteamLogin);
            RegisterRequestToServer<RequestUserRegisterMessage, ResponseSteamAuthLoginMessage>(MMORequestTypes.RequestSteamRegister, HandleRequestSteamRegister);
        }
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
        /// <summary>
        /// This is mmorpgkit's internal userid
        /// </summary>
        public string userId;
        public string accessToken;
        public long unbanTime;
        /// <summary>
        /// This is the actual username or steamid in mmorgkit
        /// </summary>
        public string username;
        public void Deserialize(NetDataReader reader)
        {
            response = reader.GetString();
            message = (UITextKeys)reader.GetPackedUShort();
            userId = reader.GetString();
            accessToken = reader.GetString();
            unbanTime = reader.GetPackedLong();
            username = reader.GetString();
        }

        public void Serialize(NetDataWriter writer)
        {
            writer.Put(response);
            writer.PutPackedUShort((ushort)message);
            writer.Put(userId);
            writer.Put(accessToken);
            writer.PutPackedLong(unbanTime);
            writer.Put(username);
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
        protected virtual bool steamCustomNameValidation(string name)
        {
            Debug.Log("Using customNameValidation");
            return true;
        }

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
#if UNITY_EDITOR || UNITY_SERVER
            string message = "";
            string steamid = request.username;
            string ticket = request.password;
            NameExtensions.overrideUsernameValidating = steamCustomNameValidation;
            //string email = request.email;
            Debug.Log("Pre API call");
            callSteamLogin(steamid, ticket, result, requestHandler);
            Debug.Log("Post API call");
#endif
        }

        protected async UniTaskVoid HandleRequestSteamRegister(
            RequestHandlerData requestHandler,
            RequestUserRegisterMessage request,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result)
        {
#if UNITY_EDITOR || UNITY_SERVER
            string message = "";
            string email = request.username;
            string password = request.password;
            NameExtensions.overrideUsernameValidating = steamCustomNameValidation;
            //string email = request.email;
            Debug.Log("Pre API call");
            //callSteamRegister(email, password, result);
            Debug.Log("Post API call");
#endif
        }


        protected async UniTaskVoid HandleRequestSteamUserLogin(string steamid,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result, RequestHandlerData requestHandler)
        {
#if UNITY_SERVER || UNITY_EDITOR
            Debug.Log("HandleRequestSteamUserLogin");
            if (disableDefaultLogin)
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE.ToString()),
                });
                return;
            }

            long connectionId = requestHandler.ConnectionId;
            DatabaseApiResult<ValidateUserLoginResp> validateUserLoginResp = await DatabaseClient.ValidateUserLoginAsync(new ValidateUserLoginReq()
            {
                Username = steamid,
                Password = steamPass
            });
            if (!validateUserLoginResp.IsSuccess)
            {
                Debug.Log("SteamLogin ValidateUserLogin Failed");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            string userId = validateUserLoginResp.Response.UserId;
            string accessToken = string.Empty;
            long unbanTime = 0;
            if (string.IsNullOrEmpty(userId))
            {
                //// Try registering user using steamID
                HandleRequestSteamUserRegister(steamid, result, requestHandler);
                return;
            }
            if (_userPeersByUserId.ContainsKey(userId) || MapContainsUser(userId))
            {
                Debug.Log("SteamLogin User Already Logged in");
                // Kick the user from game
                if (_userPeersByUserId.ContainsKey(userId))
                {
                    KickClient(_userPeersByUserId[userId].connectionId, UITextKeys.UI_ERROR_ACCOUNT_LOGGED_IN_BY_OTHER);
                    //No longer being used, atleast in 1.85
                    //ServerTransport.ServerDisconnect(_userPeersByUserId[userId].connectionId);
                }
                //TODO: ENABLE WHEN UPDATE TO 1.77
                ClusterServer.KickUser(userId, UITextKeys.UI_ERROR_ACCOUNT_LOGGED_IN_BY_OTHER);
                RemoveUserPeerByUserId(userId, out _);
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_ALREADY_LOGGED_IN,
                });
                return;
            }

            DatabaseApiResult<GetUserUnbanTimeResp> unbanTimeResp = await DatabaseClient.GetUserUnbanTimeAsync(new GetUserUnbanTimeReq()
            {
                UserId = userId
            });
            if (!unbanTimeResp.IsSuccess)
            {
                Debug.Log("SteamLogin UserUnbanTime Failed");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            unbanTime = unbanTimeResp.Response.UnbanTime;
            if (unbanTime > System.DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                Debug.Log("SteamLogin User is Banned");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_USER_BANNED,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_USER_BANNED.ToString()),
                });
                return;
            }
            CentralUserPeerInfo userPeerInfo = new CentralUserPeerInfo();
            userPeerInfo.connectionId = connectionId;
            userPeerInfo.userId = userId;
            userPeerInfo.accessToken = accessToken = Regex.Replace(System.Convert.ToBase64String(System.Guid.NewGuid().ToByteArray()), "[/+=]", "");
            _userPeersByUserId[userId] = userPeerInfo;
            _userPeers[connectionId] = userPeerInfo;
            Debug.Log("HandleRequestSteamUserLogin: " + userId + " " + connectionId + " " + accessToken + " " + userPeerInfo.accessToken);
            DatabaseApiResult updateAccessTokenResp = await DatabaseClient.UpdateAccessTokenAsync(new UpdateAccessTokenReq()
            {
                UserId = userId,
                AccessToken = accessToken
            });
            if (!updateAccessTokenResp.IsSuccess)
            {
                Debug.Log("SteamLogin UpdateAccessToken Failed");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            // Response
            result.InvokeSuccess(new ResponseSteamAuthLoginMessage()
            {
                userId = userId,
                accessToken = accessToken,
                unbanTime = unbanTime,
                response = "success",
                username = steamid
            });
#endif
        }



        protected async UniTaskVoid HandleRequestSteamUserRegister(string steamid,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result,
            RequestHandlerData requestHandler)
        {
#if UNITY_EDITOR || UNITY_SERVER
            Debug.Log("HandleRequestSteamUserRegister");
            if (disableDefaultLogin)
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_SERVICE_NOT_AVAILABLE.ToString()),
                });
                return;
            }
            string username = steamid.Trim();
            string password = steamPass;
            string email = "";
            if (!NameExtensions.IsValidUsername(username))
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INVALID_USERNAME,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INVALID_USERNAME.ToString()),
                });
                return;
            }
            //
            //RequireEmail code deleted
            //
            DatabaseApiResult<FindUsernameResp> findUsernameResp = await DatabaseClient.FindUsernameAsync(new FindUsernameReq()
            {
                Username = username
            });
            if (!findUsernameResp.IsSuccess)
            {
                Debug.Log("SteamRegister FindUsernameReq Failed");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            if (findUsernameResp.Response.FoundAmount > 0)
            {
                Debug.Log("SteamRegister Username Exists");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_USERNAME_EXISTED,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_USERNAME_EXISTED.ToString()),
                });
                return;
            }
            //
            //Removed Username and Password length and validation checks
            //
            DatabaseApiResult createResp = await DatabaseClient.CreateUserLoginAsync(new CreateUserLoginReq()
            {
                Username = username,
                Password = password,
                Email = email,
            });
            if (!createResp.IsSuccess)
            {
                Debug.Log("SteamRegister RegistrationReq Failed");
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            // Success registering, lets retry login now
            //result.InvokeSuccess(new ResponseSteamAuthLoginMessage());
            HandleRequestSteamUserLogin(steamid, result, requestHandler);
#endif
        }

    }


    public partial class MMOClientInstance : MonoBehaviour
    {
        public void RequestSteamLogin(string steamid, string ticket, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            //centralNetworkManager.RequestSteamLogin(steamid, ticket, callback);
            CentralNetworkManager.RequestSteamLogin(steamid, ticket, (responseHandler, responseCode, response) => OnRequestSteamLogin(responseHandler, responseCode, response, callback).Forget());
        }
        public void RequestSteamRegister(string email, string password, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            centralNetworkManager.RequestSteamRegister(email, password, callback);
        }

        private async UniTaskVoid OnRequestSteamLogin(ResponseHandlerData responseHandler, AckResponseCode responseCode, ResponseSteamAuthLoginMessage response, ResponseDelegate<ResponseSteamAuthLoginMessage> callback)
        {
            await UniTask.Yield();

            if (callback != null)
                callback.Invoke(responseHandler, responseCode, response);

            GameInstance.UserId = string.Empty;
            GameInstance.UserToken = string.Empty;
            GameInstance.SelectedCharacterId = string.Empty;
            if (responseCode == AckResponseCode.Success)
            {
                GameInstance.UserId = response.userId;
                GameInstance.UserToken = response.accessToken;
            }
        }

        /// <summary>
        /// Try to initialize SteamClient using pre-configured app id, if game is launched without steam, it will quit and launch from steam.
        /// Will return true if SteamClient intialized or already running, otherwise return false
        /// </summary>
        /// <returns></returns>
        public ErrorDetailsRes trySteamInit()
        {
            //TODO: Enable in production, disable during test runs
            ErrorDetailsRes err = new ErrorDetailsRes();
#if !UNITY_EDITOR
/*
            if (Steamworks.SteamClient.RestartAppIfNecessary(CentralNetworkManager.AppID))
            {
                err.error = true;
                err.code = 1;
                err.message = "Game launched outside steam, restarting it from Steam";
                StartCoroutine(DelayExitGame(3.0f));    //Exit after delay as the game is about to be relaunched from Steam
                return err;
            }
            */
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
                Steamworks.SteamClient.Init(CentralNetworkManager.AppID, true);
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
#endif
