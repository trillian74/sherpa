﻿using Microsoft.SharePoint.Client;
using System.Collections.Generic;

namespace Sherpa.Library.ContentTypes.Model
{
    public class ShContentType
    {
        private string _id;

        public string ID
        {
            get { return _id; }
            set { _id = value.ToUpper().Replace("0X01", "0x01"); }
        }

        public string InternalName { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }

        public ShTemplateInformation Template { get; set; }

        public List<string> Fields { get; set; }
        public List<string> RequiredFields { get; set; }
        public List<string> HiddenFields { get; set; }
        public List<string> RemovedFields { get; set; }

        public ShContentType()
        {
            Fields = new List<string>();
            RequiredFields = new List<string>();
            HiddenFields = new List<string>();
            RemovedFields = new List<string>();
        }

        public ContentTypeCreationInformation GetContentTypeCreationInformation()
        {
            return new ContentTypeCreationInformation
            {
                Name = InternalName,
                Id = ID,
                Group = Group,
                Description = Description
            };
        }
    }
}