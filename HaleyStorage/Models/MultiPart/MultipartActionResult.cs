using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using System;
using System.Buffers;
using System.Collections.ObjectModel;

namespace Haley.Models {
    public class MultipartActionResult : Collection<FileStreamInfo>, IActionResult {
        private readonly MultipartContent _internalContent;
        public MultipartActionResult(string subtype = "byteranges", string boundary = null) {
            if (boundary == null) {
                this._internalContent = new MultipartContent(subtype);
            } else {
                this._internalContent = new MultipartContent(subtype, boundary);
            }
        }

        public async Task ExecuteResultAsync(ActionContext context) {
            foreach (var item in this) {
                if (item.Stream != null) {
                    var content = new StreamContent(item.Stream);

                    if (item.ContentType != null) {
                        content.Headers.ContentType = new MediaTypeHeaderValue(item.ContentType);
                    }

                    if (item.FileName != null) {
                        content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment");
                        content.Headers.ContentDisposition.FileName = item.FileName;
                        content.Headers.ContentDisposition.FileNameStar = item.FileName;
                    }

                    this._internalContent.Add(content);
                }
            }

            context.HttpContext.Response.ContentLength = _internalContent.Headers.ContentLength;
            context.HttpContext.Response.ContentType = _internalContent.Headers.ContentType?.ToString();

            await _internalContent.CopyToAsync(context.HttpContext.Response.Body);
        }
    }
}
