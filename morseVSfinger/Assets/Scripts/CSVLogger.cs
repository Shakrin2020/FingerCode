using System.IO;
using UnityEngine;

public class CSVLogger : MonoBehaviour
{
    public static CSVLogger Instance { get; private set; }

    string _path;
    bool _hasHeader;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        _path = Path.Combine(Application.persistentDataPath, "xr_auth_results.csv");

        // If file doesn't exist, write header once
        if (!File.Exists(_path))
        {
            var header =
                "deviceUserId,user,method,attemptIndex,isPractice,success,durationSec,errorFlag,errorType,timestamp\n";
            File.WriteAllText(_path, header);
        }

        else
        {
            _hasHeader = true;
        }

        Debug.Log("[CSVLogger] Logging to: " + _path);
    }

    public void LogTrial(
    string user,
    string method,
    int attemptIndex,
    bool isPractice,
    bool success,
    float durationSec,
    int errorFlag = 0,
    string deviceUserId = "",   // 👈 from ESP
    string errorType = ""       // "timeout", "mismatch", etc.
)
    {
        if (string.IsNullOrEmpty(user))
            user = "UNKNOWN";

        string timestamp = System.DateTime.UtcNow.ToString("o");

        // 👇 ORDER MUST MATCH THE HEADER:
        // deviceUserId,user,method,attemptIndex,isPractice,success,durationSec,errorFlag,errorType,timestamp
        string line =
            $"{deviceUserId}," +
            $"{user}," +
            $"{method}," +
            $"{attemptIndex}," +
            $"{(isPractice ? 1 : 0)}," +
            $"{(success ? 1 : 0)}," +
            $"{durationSec:F3}," +
            $"{errorFlag}," +
            $"{errorType}," +
            $"{timestamp}\n";

        File.AppendAllText(_path, line);
        Debug.Log("[CSVLogger] " + line.Trim());
    }


}
