//----------------------------------------------------------------------------------------------------------------------------
// <copyright file="FileUploadModel.cs" company="Microsoft Corporation">
//  Copyright 2011 Microsoft Corporation
// </copyright>
// Licensed under the MICROSOFT LIMITED PUBLIC LICENSE version 1.1 (the "License"); 
// You may not use this file except in compliance with the License. 
//---------------------------------------------------------------------------------------------------------------------------
namespace WAMS.MediaLib.Models
{
    using System;
    using Microsoft.WindowsAzure.Storage;
    using System.Collections.Generic;
    using Microsoft.WindowsAzure.Storage.Blob;

    /// <summary>
    /// Model denoting file object to be uploaded to blob.
    /// </summary>
    public class FileUploadModel
    {
        /// <summary>
        /// Gets or sets the block count.
        /// </summary>
        /// <value>The block count.</value>
        public long BlockCount { get; set; }

        /// <summary>
        /// Gets or sets the size of the file.
        /// </summary>
        /// <value>The size of the file.</value>
        public long FileSize { get; set; }

        /// <summary>
        /// Gets or sets the name of the file.
        /// </summary>
        /// <value>The name of the file.</value>
        public string FileName { get; set; }

        /// <summary>
        /// Gets or sets the CloudBlockBlob reference to the media file on Azure storage.
        /// </summary>
        public CloudBlockBlob Blob { get; set; }

        /// <summary>
        /// Gets or sets the operation start time.
        /// </summary>
        /// <value>The start time.</value>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets the upload status message.
        /// </summary>
        /// <value>The upload status message.</value>
        public string UploadStatusMessage { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether upload of this instance is complete.
        /// </summary>
        /// <value>
        /// True if upload of this instance is complete; otherwise, false.
        /// </value>
        public bool IsUploadCompleted { get; set; }


        public override bool Equals(object obj)
        {
            if (obj is FileUploadModel)
            {
                FileUploadModel compareTo = obj as FileUploadModel;

                return (this.FileName == compareTo.FileName) ? true : false;
            }

            return base.Equals(obj);
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }
    }


    public class FileUploadModelComparer : IEqualityComparer<FileUploadModel>
    {
        /// <summary>
        /// Has a good distribution.
        /// </summary>
        const int _multiplier = 89;

        public bool Equals(FileUploadModel x, FileUploadModel y)
        {
            if (x.FileName == y.FileName)
            {
                return true;
            }
            else return false;
        }

        public int GetHashCode(FileUploadModel obj)
        {
            // Stores the result.
            int result = 0;

            // Don't compute hash code on null object.
            if (obj == null)
            {
                return 0;
            }

            // Get length.
            int length = obj.FileName.Length;

            // Return default code for zero-length strings [valid, nothing to hash with].
            if (length > 0)
            {
                // Compute hash for strings with length greater than 1
                char let1 = obj.FileName[0];          // First char of string we use
                char let2 = obj.FileName[length - 1]; // Final char

                // Compute hash code from two characters
                int part1 = let1 + length;
                result = (_multiplier * part1) + let2 + length;
            }
            return result;

        }
    }
}