﻿using System.Collections.Generic;

namespace Sherpa.Library.SiteHierarchy.Model
{
    public class ShList
    {
        public string Title { get; set; }
        public string Url { get; set; }

        //http://techtrainingnotes.blogspot.no/2008/01/sharepoint-registrationid-list-template.html
        public int TemplateType { get; set; }

        public bool? VersioningEnabled { get; set; }
        public bool? OnQuickLaunch { get; set; }
        public bool? Hidden { get; set; }
        public string Description { get; set; }
        public List<ShView> Views { get; set; }
        public List<string> ContentTypes { get; set; }
        public bool RemoveExisitingContentTypes { get; set; }
        public List<string> Fields { get; set; }
        public ShPermissionScheme PermissionScheme { get; set; }
        public bool AddMetadataDefaultsReceiver { get; set; }
        public bool ExportData { get; set; }

        public ShList()
        {
            Views = new List<ShView>();
            ContentTypes = new List<string>();
            Fields = new List<string>();
        }
    }
}