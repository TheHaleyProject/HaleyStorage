using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Reflection.Metadata.Ecma335;

namespace Haley.Services {
    public partial class DiskStorageService : IDiskStorageService {
        bool TrySanitizeFormat(string format, out string result) {
            result = default;

            if (string.IsNullOrWhiteSpace(format))
                return false;

            // Trim spaces first
            format = format.Trim();

            // Lowercase
            format = format.ToLowerInvariant();

            // Normalize MIME vs Extension behavior
            if (format.Contains("/")) {
                // MIME TYPE SANITIZATION
                // Remove repeated slashes
                while (format.Contains("//"))
                    format = format.Replace("//", "/");

                // Remove backslashes
                format = format.Replace("\\", "/");

                // Remove internal spaces
                format = string.Join("", format.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            } else {
                // EXTENSION SANITIZATION
                // Remove starting dots
                format = format.TrimStart('.');

                // Remove everything after the first extension if multiple
                // e.g., pdf.exe → pdf
                if (format.Contains(".")) {
                    format = format.Split('.')[0];
                }

                // Remove any invalid characters
                format = new string(format.Where(char.IsLetterOrDigit).ToArray());
            }

            if (string.IsNullOrWhiteSpace(format))
                return false;

            result = format;
            return true;
        }


        List<string> GetSource (OSSFormatType type, bool restricted) {
            switch (type) {
                case OSSFormatType.Extension:
                return restricted ? RestrictedExtensions : AllowedExtensions;
                case OSSFormatType.MimeType:
                return restricted ? RestrictedMimeTypes : AllowedMimeTypes;
                default:
                throw new ArgumentNullException(nameof(type));
            }
        }

        IOSSFormatManagement ModifyFormat(string format, OSSFormatType type, bool isAdd, bool restricted) {
            if (!TrySanitizeFormat(format, out var sanitized)) return this;
            var source = GetSource(type, restricted);
            if (isAdd && !source.Contains(sanitized)) source.Add(sanitized);
            if (!isAdd && source.Contains(sanitized)) source.Remove(sanitized);
            return this;
        }
        IOSSFormatManagement ModifyFormatRange(List<string> formats, OSSFormatType type, bool isAdd, bool restricted) {
            foreach (var format in formats) {
                ModifyFormat(format, type,isAdd,restricted); //Add only the allowed formats.
            }
            return this;
        }
        public IOSSFormatManagement AddFormat(string format, OSSFormatType type, bool restricted = false) => ModifyFormat(format,type,true,restricted);
        public IOSSFormatManagement AddFormatRange(List<string> formats, OSSFormatType type, bool restricted = false) => ModifyFormatRange(formats, type, true, restricted);
        public IOSSFormatManagement RemoveFormat(string format, OSSFormatType type, bool restricted = false) => ModifyFormat(format, type, false, restricted);

        public bool IsFormatAllowed(string format, OSSFormatType type) {
            if (!TrySanitizeFormat(format, out var sanitized)) return false;

            var allowedSource = GetSource(type, false);
            var restrictedSource = GetSource(type, true);

            //Priority 1: If Allowed Formats is present. (If present means, allowed)
            if (allowedSource != null && allowedSource.Count > 0) return allowedSource.Contains(sanitized);

            //Priority 2: If Restricted Formats is present (Should not be present means, it is allowed)
            if (restrictedSource != null && restrictedSource.Count > 0) return !restrictedSource.Contains(sanitized);
            return true; //In this case, there is not restriction, allow everything.
        }
        public bool IsFormatTypeControlled(OSSFormatType type) {
            return GetSource(type, false)?.Count > 0 || GetSource(type, true)?.Count > 0; //If either, allowed, or restricted list is not empty, then it has some sort of control in place.
        }
    }
}
