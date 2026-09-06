using Haley.Abstractions;
using Haley.Enums;
using Haley.Models;
using Haley.Utils;
using System.Reflection.Metadata.Ecma335;

namespace Haley.Services {
    /// <summary>
    /// Partial class — file format allow/deny policy.
    /// Maintains separate allowed and restricted lists for file extensions and MIME types.
    /// When an allowed list is non-empty it takes priority; when only a restricted list exists,
    /// anything absent from that list is permitted.
    /// </summary>
    public partial class StorageCoordinator : IStorageCoordinator {
        readonly object _formatPolicyLock = new();
        /// <summary>
        /// Normalises an extension or MIME type string for storage in the allow/deny lists.
        /// Extensions: lowercased, leading dots stripped, multi-extension values truncated at first dot.
        /// MIME types: lowercased, repeated slashes collapsed, spaces removed.
        /// Returns <c>false</c> when the result is empty after sanitization.
        /// </summary>
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


        /// <summary>Returns the correct internal list (allowed or restricted, extension or MIME) for the given parameters.</summary>
        List<string> GetSource (FormatControlMode type, bool restricted) {
            switch (type) {
                case FormatControlMode.Extension:
                return restricted ? RestrictedExtensions : AllowedExtensions;
                case FormatControlMode.MimeType:
                return restricted ? RestrictedMimeTypes : AllowedMimeTypes;
                default:
                throw new ArgumentNullException(nameof(type));
            }
        }

        IFileFormatPolicy ModifyFormat(string format, FormatControlMode type, bool isAdd, bool restricted) {
            if (!TrySanitizeFormat(format, out var sanitized)) return this;
            lock (_formatPolicyLock) {
                var source = GetSource(type, restricted);
                if (isAdd && !source.Contains(sanitized)) source.Add(sanitized);
                if (!isAdd && source.Contains(sanitized)) source.Remove(sanitized);
            }
            return this;
        }
        IFileFormatPolicy ModifyFormatRange(List<string> formats, FormatControlMode type, bool isAdd, bool restricted) {
            foreach (var format in formats) {
                ModifyFormat(format, type,isAdd,restricted); //Add only the allowed formats.
            }
            return this;
        }
        /// <summary>Adds a single sanitized extension or MIME type to the allowed or restricted list.</summary>
        public IFileFormatPolicy AddFormat(string format, FormatControlMode type, bool restricted = false) => ModifyFormat(format,type,true,restricted);
        /// <summary>Adds a range of sanitized extensions or MIME types to the allowed or restricted list.</summary>
        public IFileFormatPolicy AddFormatRange(List<string> formats, FormatControlMode type, bool restricted = false) => ModifyFormatRange(formats, type, true, restricted);
        /// <summary>Removes a single sanitized extension or MIME type from the allowed or restricted list.</summary>
        public IFileFormatPolicy RemoveFormat(string format, FormatControlMode type, bool restricted = false) => ModifyFormat(format, type, false, restricted);

        /// <summary>Replaces one policy list with sanitized, distinct values.</summary>
        public IFileFormatPolicy ReplaceFormats(IEnumerable<string> formats, FormatControlMode type, bool restricted = false) {
            var sanitized = (formats ?? Enumerable.Empty<string>())
                .Select(format => TrySanitizeFormat(format, out var value) ? value : null)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_formatPolicyLock) {
                var source = GetSource(type, restricted);
                source.Clear();
                source.AddRange(sanitized!);
            }
            return this;
        }

        /// <summary>Returns an immutable snapshot of one policy list.</summary>
        public IReadOnlyList<string> GetFormats(FormatControlMode type, bool restricted = false) {
            lock (_formatPolicyLock) {
                return GetSource(type, restricted).ToArray();
            }
        }

        /// <summary>
        /// Returns <c>true</c> if the format is permitted under the current policy.
        /// Priority: allowed list (if non-empty) → restricted list → default allow all.
        /// </summary>
        public bool IsFormatAllowed(string format, FormatControlMode type) {
            if (!TrySanitizeFormat(format, out var sanitized)) return false;

            lock (_formatPolicyLock) {
                var allowedSource = GetSource(type, false);
                var restrictedSource = GetSource(type, true);

                // An allow-list takes precedence; otherwise the deny-list is applied.
                if (allowedSource.Count > 0) return allowedSource.Contains(sanitized);
                if (restrictedSource.Count > 0) return !restrictedSource.Contains(sanitized);
                return true;
            }
        }
        /// <summary>
        /// Returns <c>true</c> if any allowed or restricted entries are registered for the given <paramref name="type"/>,
        /// meaning format checking is active for that mode.
        /// </summary>
        public bool IsFormatTypeControlled(FormatControlMode type) {
            lock (_formatPolicyLock) {
                return GetSource(type, false).Count > 0 || GetSource(type, true).Count > 0;
            }
        }
    }
}
