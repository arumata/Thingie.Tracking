using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Windows;
using Thingie.Tracking.DataStoring;
using Thingie.Tracking.Serialization;

namespace Thingie.Tracking
{
    public class SettingsTracker
    {
        private readonly List<TrackingConfiguration> _configurations = new List<TrackingConfiguration>();

        /// <summary>
        ///     Uses a BinarySerializer as the serializer, and a FileDataStore as the data store.
        ///     Uses the <see cref="AssemblyCompanyAttribute" /> and <see cref="AssemblyTitleAttribute" />
        ///     to construct the path for the settings file, which it combines with the user's ApplicationData
        ///     folder.
        /// </summary>
        /// <param name="baseFolder"></param>
        public SettingsTracker()
            : this(new FileDataStore(Environment.SpecialFolder.ApplicationData), new BinarySerializer())
        {
        }

        public SettingsTracker(IDataStore store, ISerializer serializer)
            : this(new ObjectStore(store, serializer))
        {
        }

        public SettingsTracker(IObjectStore objectStore)
        {
            ObjectStore = objectStore;
            WireUpAutomaticPersist();
        }

        public string Name { get; set; }

        public IObjectStore ObjectStore { get; private set; }

        #region automatic persisting

        protected virtual void WireUpAutomaticPersist()
        {
            if (Application.Current != null) //wpf
                Application.Current.Exit += (s, e) => { PersistAutomaticTargets(); };
            else //winforms
                System.Windows.Forms.Application.ApplicationExit += (s, e) => { PersistAutomaticTargets(); };
        }

        #endregion

        /// <summary>
        ///     Creates or retrieves the tracking configuration for the speficied object.
        /// </summary>
        /// <param name="target"></param>
        /// <returns></returns>
        public TrackingConfiguration Configure(object target)
        {
            var config = FindExistingConfig(target);
            if (config == null)
                _configurations.Add(config = new TrackingConfiguration(target, this));
            return config;
        }

        public void ApplyAllState()
        {
            _configurations.ForEach(c => c.Apply());
        }

        public void ApplyState(object target)
        {
            var config = FindExistingConfig(target);
            Debug.Assert(config != null);
            config.Apply();
        }

        public void PersistState(object target)
        {
            var config = FindExistingConfig(target);
            Debug.Assert(config != null);
            config.Persist();
        }

        public void PersistAutomaticTargets()
        {
            foreach (var config in _configurations.Where(cfg => cfg.Mode == PersistModes.Automatic && cfg.TargetReference.IsAlive))
                PersistState(config.TargetReference.Target);
        }

        public IEnumerable<TrackingConfiguration> FindExistingConfigsByType(object target)
        {
            var type = target.GetType();
            return _configurations.Where(cfg => cfg.TargetReference.IsAlive && type == cfg.TargetReference.Target.GetType());
        }

        public void RemoveConfiguration(TrackingConfiguration config)
        {
            _configurations.Remove(config);
        }

        public void ApplyAllByType(object target, bool exceptThisTarget = true)
        {
            var configurations = FindExistingConfigsByType(target);
            if (exceptThisTarget)
                configurations = configurations.Where(x => x != target);
            foreach (var trackingConfiguration in configurations)
            {
                trackingConfiguration.JustApply();
            }
        }

        #region private helper methods

        private TrackingConfiguration FindExistingConfig(object target)
        {
            return _configurations.SingleOrDefault(cfg => cfg.TargetReference.Target == target);
        }

        #endregion
    }
}