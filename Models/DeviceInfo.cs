using System;

namespace CMDevicesManager.Models
{
    public sealed class DeviceInfo
    {
        public string Id { get; }
        public string Name { get; }
        public string ImagePath { get; }

        public DeviceInfo(string id, string name, string imagePath)
        {
            Id = id ?? Guid.NewGuid().ToString("N");
            Name = name ?? "Unknown Device";
            ImagePath = imagePath ?? string.Empty;
        }

        public override string ToString() => Name;
    }
}