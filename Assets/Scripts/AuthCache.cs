using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface IMwaAuthCache
{
    CachedAuth Get(string pubkey);
    CachedAuth GetLatest();
    void Set(string pubkey, string authToken, string walletUriBase = "", string walletName = "", int walletType = -1);
    void Clear(string pubkey);
    void ClearAll();
}

[Serializable]
public class CachedAuth
{
    public string pubkey;
    public string authToken;
    public string walletUriBase;
    public string walletName;
    public int walletType = -1;
    public long timestamp;
}

public class AuthCache : IMwaAuthCache
{
    private const string TAG = "[AuthCache]";
    private const string CachePrefix = "mwa_auth_";
    private const string LatestKey = "mwa_auth_latest";
    private const string AllKeysKey = "mwa_auth_all_keys"; // Track all cached pubkeys

    public CachedAuth Get(string pubkey)
    {
        string key = CachePrefix + pubkey;
        Debug.Log($"{TAG} Get | START pubkey={pubkey} key={key}");

        if (!PlayerPrefs.HasKey(key))
        {
            Debug.Log($"{TAG} Get | NOT_FOUND");
            return null;
        }

        string json = PlayerPrefs.GetString(key);
        Debug.Log($"{TAG} Get | FOUND json_len={json.Length}");
        var cached = JsonUtility.FromJson<CachedAuth>(json);
        Debug.Log($"{TAG} Get | DONE pubkey={cached.pubkey} auth_token_len={cached.authToken?.Length ?? 0} timestamp={cached.timestamp}");
        return cached;
    }

    public CachedAuth GetLatest()
    {
        Debug.Log($"{TAG} GetLatest | START");

        if (!PlayerPrefs.HasKey(LatestKey))
        {
            Debug.Log($"{TAG} GetLatest | NO_LATEST_KEY");
            return null;
        }

        string pubkey = PlayerPrefs.GetString(LatestKey);
        Debug.Log($"{TAG} GetLatest | latest_pubkey={pubkey}");
        var result = Get(pubkey);
        Debug.Log($"{TAG} GetLatest | DONE found={result != null}");
        return result;
    }

    public void Set(string pubkey, string authToken, string walletUriBase = "", string walletName = "", int walletType = -1)
    {
        Debug.Log($"{TAG} Set | START pubkey={pubkey} auth_token_len={authToken?.Length ?? 0} wallet_uri_base={walletUriBase} wallet_name={walletName} wallet_type={walletType}");

        var cached = new CachedAuth
        {
            pubkey = pubkey,
            authToken = authToken,
            walletUriBase = walletUriBase,
            walletName = walletName,
            walletType = walletType,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        string json = JsonUtility.ToJson(cached);
        PlayerPrefs.SetString(CachePrefix + pubkey, json);
        PlayerPrefs.SetString(LatestKey, pubkey);

        // Track all known pubkeys for ClearAll
        var allKeys = GetAllKeys();
        if (!allKeys.Contains(pubkey))
        {
            allKeys.Add(pubkey);
            SaveAllKeys(allKeys);
        }

        PlayerPrefs.Save();
        AndroidToast.Show($"Auth cached for {pubkey[..Math.Min(8, pubkey.Length)]}...");
        Debug.Log($"{TAG} Set | DONE total_cached={allKeys.Count} timestamp={cached.timestamp}");
    }

    public void Clear(string pubkey)
    {
        Debug.Log($"{TAG} Clear | START pubkey={pubkey}");

        bool existed = PlayerPrefs.HasKey(CachePrefix + pubkey);
        PlayerPrefs.DeleteKey(CachePrefix + pubkey);

        if (PlayerPrefs.GetString(LatestKey, "") == pubkey)
            PlayerPrefs.DeleteKey(LatestKey);

        var allKeys = GetAllKeys();
        allKeys.Remove(pubkey);
        SaveAllKeys(allKeys);

        PlayerPrefs.Save();
        Debug.Log($"{TAG} Clear | DONE existed={existed} remaining={allKeys.Count}");
    }

    public void ClearAll()
    {
        Debug.Log($"{TAG} ClearAll | START");

        var allKeys = GetAllKeys();
        Debug.Log($"{TAG} ClearAll | clearing {allKeys.Count} entries");

        foreach (var pubkey in allKeys)
        {
            Debug.Log($"{TAG} ClearAll | removing pubkey={pubkey}");
            PlayerPrefs.DeleteKey(CachePrefix + pubkey);
        }

        PlayerPrefs.DeleteKey(LatestKey);
        PlayerPrefs.DeleteKey(AllKeysKey);
        PlayerPrefs.Save();

        Debug.Log($"{TAG} ClearAll | DONE");
    }

    private List<string> GetAllKeys()
    {
        if (!PlayerPrefs.HasKey(AllKeysKey))
            return new List<string>();

        string raw = PlayerPrefs.GetString(AllKeysKey);
        if (string.IsNullOrEmpty(raw))
            return new List<string>();

        return new List<string>(raw.Split(','));
    }

    private void SaveAllKeys(List<string> keys)
    {
        PlayerPrefs.SetString(AllKeysKey, string.Join(",", keys));
    }
}
