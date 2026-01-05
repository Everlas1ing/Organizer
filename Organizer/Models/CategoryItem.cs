using SQLite;

namespace Organizer.Models
{
    public class CategoryItem
    {
        [PrimaryKey, AutoIncrement]
        public int ID { get; set; }

        [Unique]
        public string Name { get; set; }

        public string ColorHex { get; set; }
    }
}