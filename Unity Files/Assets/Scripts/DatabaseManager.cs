using UnityEngine;
using SQLite4Unity3d; 
using System;
using System.IO;
using IOPath = System.IO.Path;

// MonoBehaviour allowes this class to be attached to GameObjects in Unity. Without this inheritance, the GameObject would not have access to its transforms and other Unity-specific features.
public class DatabaseManager : MonoBehaviour
{
    // private SQLiteConnection _connection;: Declares a private variable of type SQLiteConnection to hold the actual database connection object. The underscore prefix (_) is a common convention for private class members.

    // public SQLiteConnection Connection => _connection;: This is a public read-only property that provides external classes with access to the established _connection. The => syntax is a shorthand for a "getter-only" property.
    private SQLiteConnection _connection;
    public SQLiteConnection Connection => _connection;

    // Awake is called when the script instance is being loaded. This is where the database connection is initialized.
    private void Awake()
    {
        string dbName = "pheonix_mall_of_asia_stores.db"; // This db needs to be in StreamingAssets folder
        string dbPath = GetDatabasePath(dbName); // Custom method to get the correct database path based on the platform defined below

        Debug.Log("LOADING DB FROM: " + dbPath);

        _connection = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);

        Debug.Log("DATABASE LOADED SUCCESSFULLY!");
    }

    // Close the native SQLite handle explicitly instead of leaving it to the finalizer during shutdown.
    private void OnDestroy()
    {
        if (_connection == null)
            return;

        _connection.Close();
        _connection = null;
    }

    public void Init()
    {
        if (_connection != null)
            return;

        string dbPath = GetDatabasePath("pheonix_mall_of_asia_stores.db");
        _connection = new SQLiteConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create);
    }

    // Custom method to get the database path based on the platform
    private string GetDatabasePath(string dbName)
    {
        string companyName = string.IsNullOrWhiteSpace(Application.companyName) ? "MallOfAsia" : Application.companyName;
        string productName = string.IsNullOrWhiteSpace(Application.productName) ? "MallOfAsia" : Application.productName;
        string persistentRoot = Application.persistentDataPath;
        if (string.IsNullOrWhiteSpace(persistentRoot))
        {
            string localDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            persistentRoot = string.IsNullOrWhiteSpace(localDataRoot)
                ? GetPlayerDataRoot(companyName, productName)
                : IOPath.Combine(localDataRoot, companyName, productName);
            Debug.LogWarning("Unity returned an empty persistent data path; using " + persistentRoot);
        }

        try
        {
            Directory.CreateDirectory(persistentRoot);
        }
        catch (UnauthorizedAccessException)
        {
            persistentRoot = GetPlayerDataRoot(companyName, productName);
            Directory.CreateDirectory(persistentRoot);
            Debug.LogWarning("The default user data directory is not writable; using " + persistentRoot);
        }

        string filePath = IOPath.Combine(persistentRoot, dbName);

        // If DB is not already copied -> copy from StreamingAssets
        if (!File.Exists(filePath))
        {
            Debug.Log("DB not found in persistent path, copying...");

            string sourcePath = IOPath.Combine(Application.streamingAssetsPath, dbName);
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException("The store database is missing from StreamingAssets.", sourcePath);

// #if UNITY_ANDROID
//             // Android requires UnityWebRequest to read StreamingAssets
//             var loadDb = new UnityEngine.Networking.UnityWebRequest(sourcePath);
//             loadDb.downloadHandler = new UnityEngine.Networking.DownloadHandlerBuffer();
//             loadDb.SendWebRequest();

//             while (!loadDb.isDone) { }

//             File.WriteAllBytes(filePath, loadDb.downloadHandler.data);
// #else
            File.Copy(sourcePath, filePath);
// #endif

            Debug.Log("Database copied to: " + filePath);
        }

        return filePath;
    }

    private static string GetPlayerDataRoot(string companyName, string productName)
    {
        string playerDirectory = IOPath.GetFullPath(IOPath.Combine(Application.dataPath, ".."));
        return IOPath.Combine(playerDirectory, "UserData", companyName, productName);
    }
}
