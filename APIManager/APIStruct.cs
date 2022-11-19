using System.Collections;
using System.Collections.Generic;
using UnityEngine;


[System.Serializable]
public class ErrorRes
{
    public ErrorDetailsRes error;
}
[System.Serializable]
public class ErrorDetailsRes
{
    public bool error;
    public int code;
    public string message;
}
[System.Serializable]
public class SteamRes
{
    public SteamCustomRes response;
}
[System.Serializable]
public class SteamCustomRes
{
    public SteamResParams @params;          //params is a registered keyword so escaped using @
}
[System.Serializable]
public class SteamResParams
{
    public string result;
    public string steamid;
    public string ownersteamid;
    public bool vacbanned;
    public bool publisherbanned;
}