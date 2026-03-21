//using System.Net.Http.Headers;
using Haley.Abstractions;
using Haley.Models;
using Haley.Utils;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using Microsoft.Identity.Client;
using Microsoft.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using static System.Collections.Specialized.BitVector32;
using Microsoft.AspNetCore.Http;

//TRY :

//if (HasFileContentDisposition(cd)) {
//    var fileStream = new MemoryStream();
//    await section.Body.CopyToAsync(fileStream);
//    fileStream.Position = 0;

//    fileSections.Add(new MultipartSection {
//        Body = fileStream,
//        ContentDisposition = section.ContentDisposition,
//        Headers = section.Headers
//    });
//}

//SET TEMP PATH:

//var tempPath = Path.GetTempFileName();
//using var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write);
//await section.Body.CopyToAsync(fs);
//fs.Position = 0;
// Store tempPath or wrap it in a stream for later use

//MultipartReader.ReadNextSectionAsync() is forward-only.

//You can process data sections as they arrive.

//For file sections, you can either:

//    Buffer them to memory/disk immediately and store references.

//    Or stream them directly when you reach Phase 2 (if you don’t need to rewind

namespace Haley.Models {
    public class MultiPartUploader
    {
        private readonly FormOptions _defaultFormOptions = new FormOptions();
        Func<MultipartFileInfo, Task<IVaultResponse>> _fileHandler;
        Func<MultipartDataInfo, Task<bool>> _dataHandler;
        Func<MultipartValidationInfo , Task<IFeedback<long?>>> _validationHandler;
        long _defaultMaxFileSizeinMb = 0;
        bool _throwExceptions = false;
        public MultiPartUploader(Func<MultipartFileInfo, Task<IVaultResponse>> fileSectionHandler, Func<MultipartDataInfo, Task<bool>> dataSectionHandler, Func<MultipartValidationInfo, Task<IFeedback<long?>>> validationHandler, int? max_size, bool throwExceptions) {
            _fileHandler = fileSectionHandler ?? throw new ArgumentNullException(nameof(fileSectionHandler));
            _dataHandler = dataSectionHandler; //Can be empty, we dont need them
            _validationHandler = validationHandler; //Can be empty.
            _defaultMaxFileSizeinMb = max_size ?? 0;
            if (_defaultMaxFileSizeinMb < 0) _defaultMaxFileSizeinMb = 0; //Zero stands for unlimited.
            _throwExceptions = throwExceptions;
            //if (_defaultMaxFileSizeinMb > 10000) _defaultMaxFileSizeinMb = 10000; // 10 GB limit
        }

        public async Task<MultipartUploadSummary> UploadFileAsync(HttpRequest request, IVaultFileWriteRequest upRequest)
        {
             return await UploadFileAsync(request.Body, request.ContentType, upRequest);
        }
     
        public async Task<MultipartUploadSummary> UploadFileAsync(Stream stream, string contentType, IVaultFileWriteRequest upRequest) {
            var dataSections = new List<(string Key, string Value)>();
            var fileSections = new List<(string FileName, string TempPath, string cd_key, string ContentType)>();
            try {
                if (!IsMultipartContentType(contentType))
                    throw new Exception($"Expected multipart request, got {contentType}");

                if (upRequest == null)
                    throw new Exception("Upload request cannot be null.");
               
                if (upRequest.BufferSize < (1024*1024)) upRequest.BufferSize = (1024 * 1024); //We need a minimum buffer of 1 MB, Since we know for a fact that the files are first stored to temp files, we can have a reasonable buffer size of 1MB.

                var boundary = GetBoundary(MediaTypeHeaderValue.Parse(contentType), _defaultFormOptions.MultipartBoundaryLengthLimit);
                var reader = new MultipartReader(boundary, stream, upRequest.BufferSize);

                MultipartUploadSummary result = new MultipartUploadSummary();
                
                await ReadSections(reader, result, dataSections, fileSections);
                long totalBytesUploaded = await UploadFileAsync(result, upRequest, dataSections, fileSections);

                result.TotalSizeUploaded = totalBytesUploaded.ToFileSize(false);
                //Before we send the result out, we can take everythign and convert the file to readable sizes.
                //result.PassedObjects.ForEach(p => {
                //    if (p.Size > 0) p.SizeHR = p.Size.ToFileSize(false);
                //});
                return result;
            } finally {
                foreach (var item in fileSections) {
                    item.TempPath?.TryDeleteFile();
                }
            }
        }

        async Task<long> UploadFileAsync(MultipartUploadSummary result, IVaultFileWriteRequest upRequest, List<(string Key, string Value)> dataSections, List<(string FileName, string TempPath, string cd_key, string ContentType)> fileSections) {
            long totalBytesUploaded = 0;

            // ---- Handle metadata ----
            MultipartDataInfo dataInfo = null;
            var formAccumulator = new KeyValueAccumulator();
            foreach (var kv in dataSections) formAccumulator.Append(kv.Key, kv.Value);
            if(formAccumulator.KeyCount > 0) dataInfo = new MultipartDataInfo(formAccumulator.GetResults());

            if (dataInfo != null && _dataHandler != null) {
                result.Status = await _dataHandler.Invoke(dataInfo);
            } else {
                result.Status = true; //Based on the data, we might need to proceed or not. If there is no data handler, we assume everything is fine.
            }

            if (!result.Status) {
                result.Message = "Data validation failed. Upload aborted.";
                return 0;
            }

            // ---- Handle file uploads ----
            foreach (var file in fileSections) {
                if (_fileHandler == null) throw new ArgumentException("File handler is mandatory.");

                var reqClone = upRequest.Clone() as IVaultFileWriteRequest;
                reqClone?.GenerateCallId(); //For tracking purpose and also to use it for all the transactions associated with this.
                if (reqClone == null) throw new ArgumentException($"Unable to clone {nameof(IVaultFileWriteRequest)} object.");

                await using var tempStream = new FileStream(file.TempPath, FileMode.Open, FileAccess.Read);
                reqClone.FileStream = tempStream;
                reqClone.OriginalName = file.FileName;
                reqClone.SetRequestedName(file.FileName);

                IVaultResponse saveSummary = new VaultResponse() { Status = false };
                try {
                    var fileInfo = new MultipartFileInfo() {
                        Request = reqClone,
                        DataInfo = dataInfo!,
                        ContentDispositionKey = file.cd_key
                    };
                    saveSummary = await _fileHandler(fileInfo);
                } catch (Exception ex) {
                    saveSummary.Message = ex.Message;
                }

                if (saveSummary != null && saveSummary.Status) {
                    result.Passed++;
                    totalBytesUploaded += saveSummary.Size;
                    result.PassedObjects.Add(saveSummary);
                } else {
                    result.Failed++;
                    result.FailedObjects.Add(saveSummary);
                }
                DeleteTemporaryFile(file.TempPath);
            }
            return totalBytesUploaded;
        }

        void DeleteTemporaryFile(string path) {
            if (string.IsNullOrWhiteSpace(path)) return;
            //Reason why we have the _ = . Without that the compilers will return a warning that this method is not awaited. For us, it is a fire and forget task. We dont' want to await it. So, to supress that warning and also to inform the compilers that it is intentially left as is, we use the _=
            _ = Task.Run(async () => {
                try {
                    await path.TryDeleteFile(3);
                } catch {
                    //Ignore;
                }
            });
        }

        async Task ReadSections(MultipartReader reader, MultipartUploadSummary result, List<(string Key, string Value)> dataSections, List<(string FileName, string TempPath, string cd_key, string ContentType)> fileSections) {
            MultipartSection section;
                while ((section = await reader.ReadNextSectionAsync()) != null) {

                    if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var cd))
                        continue;

                    // 1️. Data sections
                    if (HasDataContentDisposition(cd)) {
                        var (key, value) = await ReadFormDataAsync(section, cd);
                        if (!string.IsNullOrEmpty(key)) dataSections.Add((key, value));
                        continue;
                    }

                    // 2️. File sections

                    if (HasFileContentDisposition(cd)) {
                        var fileName = cd.FileNameStar.HasValue ? cd.FileNameStar.Value : cd.FileName.Value;
                        var cdname = cd.Name.ToString() ?? string.Empty; //Get the key name.

                        fileName = Path.GetFileName(fileName); // sanitize

                        var sectionContentType = section.ContentType;

                        if (string.IsNullOrWhiteSpace(sectionContentType) && section.Headers.TryGetValue("Content-Type", out var headerVal)) {
                            sectionContentType = headerVal.ToString();
                        }

                        IFeedback<long?> validationFb = null;

                        // ---- External validation ----
                        if (_validationHandler != null) {
                            validationFb = await _validationHandler.Invoke(new MultipartValidationInfo() { FileName = fileName, ContentType = sectionContentType });
                            if (!validationFb.Status) {
                                var msg = $"File '{fileName}' rejected by validation handler. Reason {validationFb.Message}";
                                if (_throwExceptions) throw new Exception(msg);
                                var failedResponse = new VaultResponse {
                                    OriginalName = fileName,
                                    Status = false,
                                    Message = msg
                                };
                                result.Failed++;
                                result.FailedObjects.Add(failedResponse);

                                //Note that the skipping or ccontinue doesn't guarantee magically jumping to next section. Either we throw an exception or wait it through.
                                await section.Body.CopyToAsync(Stream.Null); // discard rejected file
                                continue; // move to next section
                            }
                        }

                        // ---- Write to temporary file ----
                        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); //In linux, we write this to /tmp/ (if used inside the container, its much better, because we store only inside the container, and not persisted anywhere.. We can even clean up all the temp folders later.

                        long maxAllowed = validationFb?.Result ?? _defaultMaxFileSizeinMb;
                        if (maxAllowed < 0) maxAllowed = 0; //0 is unlimited.
                        maxAllowed = maxAllowed * 1024 * 1024; //In Bytes.

                        long totalRead = 0;
                        var buffer = new byte[1024 * 500]; // In 80Kb chunks buffer, it takes 29 seconds for 275 MB file. Though it doesn't make much difference, let us keep it as 500KB chunk.
                    bool skipFile = false;
                        await using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize:1024*1024*2,useAsync:true)) { // 1 MB buffer is reasonable.
                            int read;
                            while ((read = await section.Body.ReadAsync(buffer, 0, buffer.Length)) > 0) {
                                totalRead += read;

                                // Check file size while streaming
                                if (maxAllowed > 0 && totalRead > maxAllowed) {
                                    fs.Dispose();
                                    //Delete file here itself.
                                    DeleteTemporaryFile(tempPath);
                                    var msg = $"File '{fileName}' exceeds default limit {maxAllowed / 1024 / 1024} MB.";
                                    if (_throwExceptions) throw new Exception(msg);

                                    var failedResponse = new VaultResponse {
                                        OriginalName = fileName,
                                        Status = false,
                                        Message = msg
                                    };
                                    result.Failed++;
                                    result.FailedObjects.Add(failedResponse);
                                    await section.Body.CopyToAsync(Stream.Null); // discard rest
                                    skipFile = true;
                                    break;
                                }
                                await fs.WriteAsync(buffer.AsMemory(0, read)); //Write the file to local
                            }
                        }

                        if (skipFile) continue; //Continue to next section.

                        fileSections.Add((fileName, tempPath, cdname, sectionContentType));
                    }
                }
        }

        // Content-Type: multipart/form-data; boundary="----WebKitFormBoundarymx2fSWqWSd0OxQqq"
        // The spec says 70 characters is a reasonable limit.
        string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit) {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary);
            if (string.IsNullOrWhiteSpace(boundary.Value)) throw new InvalidDataException("Missing content-type boundary.");
            if (boundary.Length > lengthLimit) throw new InvalidDataException($"Multipart boundary length limit {lengthLimit} exceeded.");
            return boundary.Value;
        }

        Encoding GetEncoding(MultipartSection section) {
            var hasMediaTypeHeader = MediaTypeHeaderValue.TryParse(section.ContentType, out var mediaType);
            // UTF-7 is insecure and should not be honored. UTF-8 will succeed in most cases.
            if (!hasMediaTypeHeader || Encoding.UTF7.Equals(mediaType.Encoding)) return Encoding.UTF8;
            return mediaType.Encoding;
        }

         async Task<(string key, string value)> ReadFormDataAsync(MultipartSection section, ContentDispositionHeaderValue cd) {
            try {
                string key = HeaderUtilities.RemoveQuotes(cd.Name!).Value;
                var encoding = GetEncoding(section);
                using var reader = new StreamReader(section.Body, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var value = await reader.ReadToEndAsync();
                return (key, value == "undefined" ? string.Empty : value);
            } catch {
                return (null, null);
            }
        }

        bool HasDataContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="key";
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && string.IsNullOrEmpty(contentDisposition.FileName.Value)
                   && string.IsNullOrEmpty(contentDisposition.FileNameStar.Value);
        }

        bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && (!string.IsNullOrEmpty(contentDisposition.FileName.Value)
                       || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value));
        }

        bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}