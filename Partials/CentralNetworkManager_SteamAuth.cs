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
using System.Text.RegularExpressions;

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
            callSteamLogin(steamid, ticket, result, requestHandler);
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


        protected async UniTaskVoid HandleRequestSteamUserLogin(string steamid,
            RequestProceedResultDelegate<ResponseSteamAuthLoginMessage> result, RequestHandlerData requestHandler)
        {
#if UNITY_STANDALONE && !CLIENT_BUILD
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
            AsyncResponseData<ValidateUserLoginResp> validateUserLoginResp = await DbServiceClient.ValidateUserLoginAsync(new ValidateUserLoginReq()
            {
                Username = steamid,
                Password = SteamConfig.steamPass
            });
            if (!validateUserLoginResp.IsSuccess)
            {
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
                /*
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INVALID_USERNAME_OR_PASSWORD,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INVALID_USERNAME_OR_PASSWORD.ToString()),
                });
                return;
                */
                //
                // Try registering user using steamID
                //
                HandleRequestSteamUserRegister(steamid, result, requestHandler);
                return;
            }
            if (userPeersByUserId.ContainsKey(userId) || MapContainsUser(userId))
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_ALREADY_LOGGED_IN,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_ALREADY_LOGGED_IN.ToString()),
                });
                return;
            }
            bool emailVerified = true;
            if (requireEmailVerification)
            {
                AsyncResponseData<ValidateEmailVerificationResp> validateEmailVerificationResp = await DbServiceClient.ValidateEmailVerificationAsync(new ValidateEmailVerificationReq()
                {
                    UserId = userId
                });
                if (!validateEmailVerificationResp.IsSuccess)
                {
                    result.InvokeError(new ResponseSteamAuthLoginMessage()
                    {
                        message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                        response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                    });
                    return;
                }
                emailVerified = validateEmailVerificationResp.Response.IsPass;
            }
            AsyncResponseData<GetUserUnbanTimeResp> unbanTimeResp = await DbServiceClient.GetUserUnbanTimeAsync(new GetUserUnbanTimeReq()
            {
                UserId = userId
            });
            if (!unbanTimeResp.IsSuccess)
            {
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
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_USER_BANNED,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_USER_BANNED.ToString()),
                });
                return;
            }
            if (!emailVerified)
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_EMAIL_NOT_VERIFIED,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_EMAIL_NOT_VERIFIED.ToString()),
                });
                return;
            }
            CentralUserPeerInfo userPeerInfo = new CentralUserPeerInfo();
            userPeerInfo.connectionId = connectionId;
            userPeerInfo.userId = userId;
            userPeerInfo.accessToken = accessToken = Regex.Replace(System.Convert.ToBase64String(System.Guid.NewGuid().ToByteArray()), "[/+=]", "");
            userPeersByUserId[userId] = userPeerInfo;
            userPeers[connectionId] = userPeerInfo;
            AsyncResponseData<EmptyMessage> updateAccessTokenResp = await DbServiceClient.UpdateAccessTokenAsync(new UpdateAccessTokenReq()
            {
                UserId = userId,
                AccessToken = accessToken
            });
            if (!updateAccessTokenResp.IsSuccess)
            {
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
#if UNITY_STANDALONE && !CLIENT_BUILD
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
            string password = SteamConfig.steamPass;
            string email = "";
            if (!NameValidating.ValidateUsername(username))
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
            AsyncResponseData<FindUsernameResp> findUsernameResp = await DbServiceClient.FindUsernameAsync(new FindUsernameReq()
            {
                Username = username
            });
            if (!findUsernameResp.IsSuccess)
            {
                result.InvokeError(new ResponseSteamAuthLoginMessage()
                {
                    message = UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR,
                    response = LanguageManager.GetText(UITextKeys.UI_ERROR_INTERNAL_SERVER_ERROR.ToString()),
                });
                return;
            }
            if (findUsernameResp.Response.FoundAmount > 0)
            {
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
            AsyncResponseData<EmptyMessage> createResp = await DbServiceClient.CreateUserLoginAsync(new CreateUserLoginReq()
            {
                Username = username,
                Password = password,
                Email = email,
            });
            if (!createResp.IsSuccess)
            {
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
                StartCoroutine(DelayExitGame(3.0f));    //Exit after delay as the game is about to be relaunched from Steam
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