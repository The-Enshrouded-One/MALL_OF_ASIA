using UnityEngine;
using System.Linq;

public class TestQuery : MonoBehaviour
{
    public DatabaseManager db;

    void Start()
    {
        // Get all stores
        var stores = db.Connection.Table<final_stores_dataset_v3>().ToList(); // Table<T>() method retrieves all records from the specified table and maps them to instances of the final_stores_dataset_v3 class.
        Debug.Log("Number of stores: " + stores.Count);

        // Get a store by normalized_name
        var store = db.Connection.Table<final_stores_dataset_v3>()
            .Where(s => s.normalized_name == "parcosluxe")
            .FirstOrDefault();

        if (store != null)
            Debug.Log("FOUND STORE: " + store.store_name + " on floor " + store.floor_number);
    }
}
