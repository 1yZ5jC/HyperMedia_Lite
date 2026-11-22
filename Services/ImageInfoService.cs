using HyperMedia_Lite.Models;
using System;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace HyperMedia_Lite.Services
{
    /// <summary>读取图片基本信息与 EXIF，仅依赖 WinRT。</summary>
    public static class ImageInfoService
    {
        public static async Task<ImageInfoModel> LoadAsync(MediaItem item)
        {
            if (item?.File == null)
            {
                return null;
            }

            var info = new ImageInfoModel
            {
                FileName = item.File.Name,
                FilePath = item.File.Path,
                FileType = item.File.ContentType
            };

            try
            {
                var basic = await item.File.GetBasicPropertiesAsync();
                info.SizeBytes = basic.Size;
                info.DateModified = basic.DateModified;
            }
            catch (Exception)
            {
                // ignored
            }

            try
            {
                var image = await item.File.Properties.GetImagePropertiesAsync();
                info.Width = image.Width;
                info.Height = image.Height;
                info.CameraManufacturer = image.CameraManufacturer;
                info.CameraModel = image.CameraModel;
                info.DateTaken = image.DateTaken;
                info.Title = image.Title;
                info.Latitude = image.Latitude;
                info.Longitude = image.Longitude;
            }
            catch (Exception)
            {
                // ignored
            }

            try
            {
                await LoadAdvancedExifAsync(item, info);
            }
            catch (Exception)
            {
                // 进阶 EXIF 不可读时忽略
            }

            return info;
        }

        private static async Task LoadAdvancedExifAsync(MediaItem item, ImageInfoModel info)
        {
            using (var stream = await item.File.OpenReadAsync())
            {
                var decoder = await BitmapDecoder.CreateAsync(stream);
                var properties = await decoder.BitmapProperties.GetPropertiesAsync(new[]
                {
                    "System.Photo.ExposureTime",
                    "System.Photo.FNumber",
                    "System.Photo.FocalLength",
                    "System.Photo.IsoSpeed"
                });

                if (properties.TryGetValue("System.Photo.ExposureTime", out var exposure) && exposure.Value != null)
                {
                    info.ExposureSeconds = Convert.ToDouble(exposure.Value);
                }
                if (properties.TryGetValue("System.Photo.FNumber", out var aperture) && aperture.Value != null)
                {
                    info.FNumber = Convert.ToDouble(aperture.Value);
                }
                if (properties.TryGetValue("System.Photo.FocalLength", out var focal) && focal.Value != null)
                {
                    info.FocalLength = Convert.ToDouble(focal.Value);
                }
                if (properties.TryGetValue("System.Photo.IsoSpeed", out var iso) && iso.Value != null)
                {
                    info.IsoSpeed = Convert.ToUInt16(iso.Value);
                }
            }
        }
    }
}
