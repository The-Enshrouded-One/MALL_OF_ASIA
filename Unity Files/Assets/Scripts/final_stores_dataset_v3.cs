using SQLite4Unity3d;

public class final_stores_dataset_v3 // The name of the table in the database
{
    public string normalized_name { get; set; }
    public int floor_number { get; set; }
    public string store_name { get; set; }
    public float Centroid_X { get; set; }
    public float Centroid_Y { get; set; }
    public float BBox_X { get; set; }
    public float BBox_Y { get; set; }
    public float BBox_Width { get; set; }
    public float BBox_Height { get; set; }
    public string category { get; set; }
    public string link { get; set; }
    public string floor_name { get; set; }
    public string logo { get; set; }
    public string description { get; set; }
    public string contact { get; set; }
    public string email { get; set; }
    public string timing { get; set; }
    public string images_0 { get; set; }
    public string images_1 { get; set; }
    public string images_2 { get; set; }
    public string images_3 { get; set; }
    public string more_like_this_0 { get; set; }
    public string more_like_this_1 { get; set; }
    public string more_like_this_2 { get; set; }
    public string more_like_this_3 { get; set; }
    public string match_source { get; set; }
}
