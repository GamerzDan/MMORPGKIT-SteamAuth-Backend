** FOR MMORPGKIT v1.88+ ALWAYS MAKE SURE TO DO A DEDICATED SERVER BUILD DUE TO UNITY_SERVER DEFINES **

# MMORPGKIT-SteamAuth-Backend
Use Steam as a Authentication (login/register) backend for [MMORPGKIT](https://assetstore.unity.com/packages/templates/systems/mmorpg-kit-2d-3d-survival-110188) for Unity.

## Features  

- Totally drag-and-drop MMORPGKIT Server Authorative login and registration using Steam
- Uses Steam's AuthSessionTickets from client to server side, server validates the ticket from Valve's API to determine if client is really the steamid user
- VAC and publisher ban checks serverside to disallow login/registration
- Simple hack detection if client sends a different steamid than the steamid received from Valve's AuthSessionTicket
- Mmorpgkit's registration and login flow is completely server based in this addon, client cannot request registration/login so it cannot manipulate steamid after validation
- Server first tries to login the user using steamid. 
If it fails, server tries to register the user directly and retries to log him in again.
- No core changes required to the kit. No Setup required. **You only need to set your steam api key and a placeholder/fix password for server's mmorpgkit user registration**
- The addon auto-initiates steam login (via onEnable() in partial UILogin class), its recommended to completely hide/remove your login and registration boxes and buttons)


![image](https://user-images.githubusercontent.com/3790163/189447442-679b7364-ea7a-4131-8735-f6f9bc278f7c.png)



## Workflow
The below flow(s) works linearly as long as each step reports success, if any step reports error or false, that step reverts back to client with the error message and displays it to the client.  
client(authticket)->server(validateValveAPI)->(SUCCESS)->server(checkBans)->server(mmokitregister/login)

## Required (install them before installing this addon)
#### MMORPGKIT   (tested on v1.76)
https://assetstore.unity.com/packages/templates/systems/mmorpg-kit-2d-3d-survival-110188
#### Rest Client for Unity (tested on 2.62)
https://assetstore.unity.com/packages/tools/network/rest-client-for-unity-102501        
#### FacePunch.Steamworks (INCLUDED IN REPO, Using release 2.3.2 which includes the steamsdk already)
https://github.com/Facepunch/Facepunch.Steamworks
#### Steamworks Account/Access (for steam appid and publisher api key)
https://partner.steamgames.com/

---

## Setup
0. Create and get your Steam Publisher API Key for your appid (https://partner.steamgames.com/doc/webapi_overview/auth#create_publisher_key)  
Also note down or copy your AppID from SteamWorks.    
You need to save/replace the AppID and SteamWebKey in **CentralNetworkManager_APIManager**.cs (under APIManager folder) with your game's appID and Publisher API Key you just copied above.    
In same **CentralNetworkManager_APIManager**.cs file, also set the **steamPass** value which is a fixed password set for all accounts that will be registered in the MMORPGKit's internal database.
![enter image description here](https://i.imgur.com/ZCVFmsY.jpeg)      

-----------
You can also set the appid and steamwebkey in your serverConfig.json (this takes priority over setting the same in the CentralNetworkManager_APIManager script.    
![image](https://user-images.githubusercontent.com/3790163/190643269-243202db-00a1-4792-93d1-08e6622f727c.png)

    
1. Increase username character length limit to **255** in **CentralNetworkManager** (via Unity Inspector)
2. Install other Required Dependencies
3. Drag and Drop this addon to your project (under Assets folder or any sub-folder within it)
4. Edit the UIMmoLogin.cs `(UnityMultiplayerARPG/MMO/Scripts/MMOGame/UI/)` classes of the MMORPGKIT to partial classes  
 ```
 Change
 public class UIMmoLogin : UIBase
 to
 public partial class UIMmoLogin : UIBase
 ```
 5. To initiate the SteamAuthentication system, you need to call `tryMMOLogin();` This is currently auto-called from OnEnable() method (line 24~) in **UIMmoLogin_SteamAuth.cs** file. 
Ideally you should disable/hide your login and registration windows and buttons, or can manually call that method as needed.
