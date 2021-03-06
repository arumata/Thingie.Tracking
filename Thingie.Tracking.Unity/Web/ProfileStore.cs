﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using System.Runtime.Serialization;
using System.Web.Profile;
using Thingie.Tracking.DataStoring;

namespace Thingie.Tracking.Unity.Web
{
    /// <summary>
    /// Since there is no option to add dynamic properties to an ASP.NET profile, 
    /// we can work arround that by creating a dictionary property in which we can 
    /// store all the data we want. We define this class only because it's
    /// not possible to use Dictionary<string, byte[]> directly because we can't specify 
    /// generic classes in the web.config profile section.
    /// </summary>
    [Serializable]
    public class TrackedData : Dictionary<string, byte[]>
    {
        public TrackedData(){}
        protected TrackedData(SerializationInfo info, StreamingContext ctx) : base(info, ctx) { }
    }

    public class ProfileStore : IDataStore
    {
        string _profileDataPropertyName = "TrackingData";
        /// <summary>
        /// The name of the property that we use to store the data in the Profile object. The property must be of type <see cref="TrackedData"/>
        /// </summary>
        public string ProfileDataPropertyName
        {
            get { return _profileDataPropertyName; }
            set { _profileDataPropertyName = value; }
        }

        private TrackedData GetDataObject()
        {
            return (TrackedData)HttpContext.Current.Profile.GetPropertyValue(_profileDataPropertyName);
        }

        #region IDataStore Members

        public bool ContainsKey(string identifier)
        {
            return GetDataObject().ContainsKey(identifier);
        }

        public byte[] GetData(string identifier)
        {
            return (byte[])GetDataObject()[identifier];
        }

        public void SetData(byte[] data, string identifier)
        {
            GetDataObject()[identifier] = data;
            HttpContext.Current.Profile.Save();
        }

        public void RemoveData(string identifier)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}
