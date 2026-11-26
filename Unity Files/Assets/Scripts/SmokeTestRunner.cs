using System;
using System.Collections;
using System.IO;
using UnityEngine;
using IOPath = System.IO.Path;

/// <summary>
/// Optional automated check for the built player. It does nothing unless the player is started with
///   MallOfAsia.exe -mallSmokeTest "nike;lifestyle;inoxmegaplex" [-mallSmokeTestOut folder]
/// For each store it runs the normal search, waits for the route to finish, saves a screenshot,
/// logs a "SMOKE" line with the outcome and finally quits with exit code 0 (all passed) or 1.
/// </summary>
public class SmokeTestRunner : MonoBehaviour
{
    string[] stores;
    string outputFolder;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        string storeList = GetArgument("-mallSmokeTest");
        if (string.IsNullOrWhiteSpace(storeList))
            return;

        var runner = new GameObject("Smoke Test Runner").AddComponent<SmokeTestRunner>();
        runner.stores = storeList.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        runner.outputFolder = GetArgument("-mallSmokeTestOut") ?? IOPath.Combine(Application.dataPath, "..", "SmokeTest");
        Directory.CreateDirectory(runner.outputFolder);
    }

    static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    IEnumerator Start()
    {
        Application.runInBackground = true;
        yield return new WaitForSeconds(3f);

        SearchStoreUI search = FindFirstObjectByType<SearchStoreUI>();
        Unit unit = FindFirstObjectByType<Unit>();
        if (search == null || unit == null || search.inputField == null)
        {
            Debug.LogError("SMOKE FAIL setup: search UI, input field or seeker is missing.");
            Application.Quit(1);
            yield break;
        }

        // Optional fixed camera positions for visual comparison: -mallViewpoints "x,y,z,yaw;x,y,z,yaw"
        string viewpoints = GetArgument("-mallViewpoints");
        if (!string.IsNullOrWhiteSpace(viewpoints))
        {
            Vector3 homePosition = unit.transform.position;
            Quaternion homeRotation = unit.transform.rotation;
            string[] poses = viewpoints.Split(';');
            for (int v = 0; v < poses.Length; v++)
            {
                string[] p = poses[v].Split(',');
                if (p.Length < 4)
                    continue;
                float Parse(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
                unit.transform.SetPositionAndRotation(new Vector3(Parse(p[0]), Parse(p[1]), Parse(p[2])), Quaternion.Euler(0f, Parse(p[3]), 0f));
                yield return new WaitForSeconds(0.5f);
                yield return new WaitForEndOfFrame();
                SaveScreen(IOPath.Combine(outputFolder, $"view_{v:00}.bmp"));
            }
            unit.transform.SetPositionAndRotation(homePosition, homeRotation);
            yield return null;
        }

        string suggestDemo = GetArgument("-mallSuggestDemo");
        if (!string.IsNullOrWhiteSpace(suggestDemo))
        {
            search.inputField.text = suggestDemo;
            yield return new WaitForSeconds(0.5f);
            yield return new WaitForEndOfFrame();
            SaveScreen(IOPath.Combine(outputFolder, "suggestions.bmp"));
        }

        unit.speed = 9f;
        unit.acceleration = 4f;
        string lastStatus = "";
        unit.StatusChanged += message => lastStatus = message;

        int failures = 0;
        for (int i = 0; i < stores.Length; i++)
        {
            string store = stores[i].Trim();
            lastStatus = "";
            Vector3 start = unit.transform.position;
            int startFloor = unit.currentFloor;

            search.inputField.text = store;
            search.OnSearchButton();

            float deadline = Time.realtimeSinceStartup + 120f;
            bool tookRouteShot = false;
            bool tookLiftShot = false;
            bool usedLift = false;
            Vector3[] lastDumped = null;
            int legIndex = 0;
            string lineInfo = "none";
            while (Time.realtimeSinceStartup < deadline &&
                   !lastStatus.StartsWith("You have arrived") &&
                   !lastStatus.StartsWith("No walkable") &&
                   !lastStatus.Contains("unavailable") &&
                   !lastStatus.StartsWith("No store matched") &&
                   !lastStatus.Contains("invalid floor") &&
                   !lastStatus.Contains("failed"))
            {
                if (lastStatus.StartsWith("Going ") && !usedLift)
                {
                    usedLift = true;
                    yield return new WaitForSeconds(1.6f);
                    yield return new WaitForEndOfFrame();
                    SaveScreen(IOPath.Combine(outputFolder, $"{i:00}_{store}_riding.bmp"));
                }
                if (!tookLiftShot && lastStatus.StartsWith("Lift doors opening"))
                {
                    yield return new WaitForSeconds(0.7f);
                    yield return new WaitForEndOfFrame();
                    SaveScreen(IOPath.Combine(outputFolder, $"{i:00}_{store}_lift.bmp"));
                    tookLiftShot = true;
                }
                if (lastStatus.StartsWith("Route ready") && unit.CurrentPath != null && unit.CurrentPath != lastDumped)
                {
                    lastDumped = unit.CurrentPath;
                    var lines = new System.Text.StringBuilder();
                    foreach (Vector3 p in lastDumped)
                        lines.AppendLine(p.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                         p.y.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," +
                                         p.z.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    File.WriteAllText(IOPath.Combine(outputFolder, $"{i:00}_{store}_path_{legIndex++}.csv"), lines.ToString());
                }
                if (!tookRouteShot && lastStatus.StartsWith("Route ready"))
                {
                    // Let the seeker turn toward the route before capturing it.
                    yield return new WaitForSeconds(0.6f);
                    yield return new WaitForEndOfFrame();
                    SaveScreen(IOPath.Combine(outputFolder, $"{i:00}_{store}_route.bmp"));
                    tookRouteShot = true;
                    RouteGuide guide = unit.Guide;
                    lineInfo = guide == null ? "missing" : $"visible={guide.RouteVisible},points={guide.RoutePointCount}";
                }
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            SaveScreen(IOPath.Combine(outputFolder, $"{i:00}_{store}_end.bmp"));

            bool passed = lastStatus.StartsWith("You have arrived") && unit.currentFloor == ExpectedFloor(store, startFloor, unit) && (unit.currentFloor == startFloor || usedLift);
            if (!passed)
                failures++;
            Debug.Log($"SMOKE {(passed ? "PASS" : "FAIL")} store={store} fromFloor={startFloor} toFloor={unit.currentFloor} " +
                      $"start={start} end={unit.transform.position} lift={usedLift} status=\"{lastStatus}\" routeLine={lineInfo}");
            yield return new WaitForSeconds(1f);
        }

        Debug.Log($"SMOKE DONE passed={stores.Length - failures}/{stores.Length}");
        yield return new WaitForSeconds(1f);
        Application.Quit(failures == 0 ? 0 : 1);
    }

    // The project does not include the ScreenCapture / ImageConversion modules, so write a plain 24-bit BMP.
    static void SaveScreen(string path)
    {
        int width = Screen.width, height = Screen.height;
        var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        Color32[] pixels = texture.GetPixels32();
        Destroy(texture);

        int rowSize = (width * 3 + 3) & ~3;
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((byte)'B'); writer.Write((byte)'M');
            writer.Write(54 + rowSize * height); writer.Write(0); writer.Write(54);
            writer.Write(40); writer.Write(width); writer.Write(height);
            writer.Write((short)1); writer.Write((short)24); writer.Write(0);
            writer.Write(rowSize * height); writer.Write(2835); writer.Write(2835); writer.Write(0); writer.Write(0);
            var row = new byte[rowSize];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color32 c = pixels[y * width + x];
                    row[x * 3] = c.b; row[x * 3 + 1] = c.g; row[x * 3 + 2] = c.r;
                }
                writer.Write(row);
            }
        }
    }

    // The floor the store is on, read from the same database the search uses.
    static int ExpectedFloor(string store, int fallback, Unit unit)
    {
        var db = FindFirstObjectByType<DatabaseManager>();
        if (db == null || db.Connection == null)
            return unit.currentFloor;
        string key = store.ToLowerInvariant();
        foreach (var row in db.Connection.Table<final_stores_dataset_v3>())
            if (row.normalized_name == key)
                return row.floor_number;
        return fallback;
    }
}
