using System;
using System.Collections.Generic;

namespace HyperMedia_Lite.Models
{
    /// <summary>图片元数据快照，绑定到信息面板。</summary>
    public sealed class ImageInfoModel
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string FileType { get; set; }
        public DateTimeOffset DateModified { get; set; }
        public ulong SizeBytes { get; set; }

        public string FormattedSize
        {
            get
            {
                double size = SizeBytes;
                string[] units = { "B", "KB", "MB", "GB" };
                int i = 0;
                while (size >= 1024 && i < units.Length - 1)
                {
                    size /= 1024;
                    i++;
                }
                return $"{size:0.##} {units[i]}";
            }
        }

        public uint Width { get; set; }
        public uint Height { get; set; }
        public string Dimensions => Width > 0 && Height > 0 ? $"{Width} × {Height}" : "—";

        public string CameraManufacturer { get; set; }
        public string CameraModel { get; set; }
        public DateTimeOffset DateTaken { get; set; }
        public string Title { get; set; }

        public double? ExposureSeconds { get; set; }
        public double? FNumber { get; set; }
        public double? FocalLength { get; set; }
        public ushort? IsoSpeed { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }

        public string ExposureText => ExposureSeconds.HasValue ? FormatExposure(ExposureSeconds.Value) : "—";
        public string FNumberText => FNumber.HasValue ? $"f/{FNumber.Value:0.##}" : "—";
        public string FocalLengthText => FocalLength.HasValue ? $"{FocalLength.Value:0.##} mm" : "—";
        public string IsoSpeedText => IsoSpeed.HasValue ? $"ISO {IsoSpeed.Value}" : "—";
        public string GpsText => Latitude.HasValue && Longitude.HasValue
            ? $"{Latitude.Value:0.######}°, {Longitude.Value:0.######}°" : "—";

        private static string FormatExposure(double seconds)
        {
            if (seconds <= 0) return "—";
            if (seconds >= 1) return $"{seconds:0.##} s";
            int denominator = (int)Math.Round(1.0 / seconds);
            return $"1/{denominator} s";
        }

        public IReadOnlyList<InfoRow> BuildRows()
        {
            return new List<InfoRow>
            {
                new InfoRow("文件名", FileName ?? "—"),
                new InfoRow("路径", FilePath ?? "—"),
                new InfoRow("类型", FileType ?? "—"),
                new InfoRow("大小", FormattedSize),
                new InfoRow("修改时间", DateModified == DateTimeOffset.MinValue ? "—" : DateModified.ToString("yyyy-MM-dd HH:mm")),
                new InfoRow("尺寸", Dimensions),
                new InfoRow("拍摄时间", DateTaken == DateTimeOffset.MinValue ? "—" : DateTaken.ToString("yyyy-MM-dd HH:mm")),
                new InfoRow("相机", string.IsNullOrEmpty(CameraModel) ? "—" : $"{CameraManufacturer ?? string.Empty} {CameraModel}".Trim()),
                new InfoRow("光圈", FNumberText),
                new InfoRow("快门", ExposureText),
                new InfoRow("ISO", IsoSpeedText),
                new InfoRow("焦距", FocalLengthText),
                new InfoRow("GPS", GpsText)
            };
        }
    }

    public sealed class InfoRow
    {
        public string Key { get; }
        public string Value { get; }

        public InfoRow(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}
