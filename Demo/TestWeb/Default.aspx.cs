﻿using System;
using Thingie.Tracking;
using Thingie.Tracking.Attributes;
using Thingie.Tracking.Unity.Web;

namespace TestWeb
{
    public partial class _Default : System.Web.UI.Page
    {
        [Trackable(TrackerName = AspNetTrackerNames.SESSION)]
        public int Counter { get; set; }

        protected void Page_Load(object sender, EventArgs e)
        {
        }

        protected void Button1_Click(object sender, EventArgs e)
        {
            Counter++;
        }

        protected override void OnPreRender(EventArgs e)
        {
            lblCounter.Text = Counter.ToString();
            base.OnPreRender(e);
        }
    }
}
