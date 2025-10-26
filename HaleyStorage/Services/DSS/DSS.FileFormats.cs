using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using System.Reflection.Metadata.Ecma335;

namespace Haley.Services {
    public partial class DiskStorageService : IDiskStorageService {
        bool TrySanitizeFormat(string format, out string result) {
            result = format;
            if (string.IsNullOrWhiteSpace(format)) return false;
            result = format.TrimStart('.');
            result = result.ToLower();
            result = result.Trim(); //remove all the leading and trailing spaces.
            return true; ;
        }

        IOSSFormatManagement AddFormat(string format, List<string> source) {
            if (!TrySanitizeFormat(format, out var sanitized)) return this;
            if (!source.Contains(sanitized)) source.Add(sanitized);
            return this;
        }

        IOSSFormatManagement AddFormatRange(List<string> formats, List<string> source) {
            foreach (var format in formats) {
                AddFormat(format,source); //Add only the allowed formats.
            }
            return this;
        }

        IOSSFormatManagement RemoveFormat(string format, List<string> source) {
            if (!TrySanitizeFormat(format, out var sanitized)) return this;
            if (source.Contains(sanitized)) source.Remove(sanitized);
            return this;
        }

        public IOSSFormatManagement AddAllowedFormat(string format) => AddFormat(format, AllowedFormats);
        public IOSSFormatManagement AddAllowedFormatRange(List<string> formats) => AddFormatRange(formats, AllowedFormats);
        public IOSSFormatManagement RemoveAllowedFormat(string format) => RemoveFormat(format, AllowedFormats);
        public IOSSFormatManagement AddRestrictedFormat(string format) => AddFormat(format, RestrictedFormats);
        public IOSSFormatManagement AddRestrictedFormatRange(List<string> formats) => AddFormatRange(formats, RestrictedFormats);
        public IOSSFormatManagement RemoveRestrictedFormat(string format) => RemoveFormat(format, RestrictedFormats);
        public bool IsFormatAllowed(string format) {
            if (!TrySanitizeFormat(format, out var sanitized)) return false;

            //Priority 1: If Allowed Formats is present. (If present means, allowed)
            if (AllowedFormats != null && AllowedFormats.Count > 0) return AllowedFormats.Contains(sanitized);

            //Priority 2: If Restricted Formats is present (Should not be present means, it is allowed)
            if (RestrictedFormats != null && RestrictedFormats.Count > 0) return !RestrictedFormats.Contains(sanitized);
            return true; //In this case, there is not restriction, allow everything.
        }
    }
}
