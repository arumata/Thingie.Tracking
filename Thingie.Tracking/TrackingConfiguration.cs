using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using Thingie.Tracking.Attributes;

namespace Thingie.Tracking
{
    public enum PersistModes
    {
        /// <summary>
        ///     State is persisted automatically upon application close
        /// </summary>
        Automatic,

        /// <summary>
        ///     State is persisted only upon request
        /// </summary>
        Manual
    }

    //public static class PropertyInfoExtensions
    //{
    //    public static Func<T, object> GetValueGetter<T>(this PropertyInfo propertyInfo)
    //    {
    //        if (typeof(T) != propertyInfo.DeclaringType)
    //        {
    //            throw new ArgumentException();
    //        }

    //        var instance = Expression.Parameter(propertyInfo.DeclaringType, "i");
    //        var property = Expression.Property(instance, propertyInfo);
    //        var convert = Expression.TypeAs(property, typeof(object));
    //        return (Func<T, object>)Expression.Lambda(convert, instance).Compile();
    //    }

    //    public static Action<T, object> GetValueSetter<T>(this PropertyInfo propertyInfo)
    //    {
    //        if (typeof(T) != propertyInfo.DeclaringType)
    //        {
    //            throw new ArgumentException();
    //        }

    //        var instance = Expression.Parameter(propertyInfo.DeclaringType, "i");
    //        var argument = Expression.Parameter(typeof(object), "a");
    //        var setterCall = Expression.Call(
    //            instance,
    //            propertyInfo.GetSetMethod(),
    //            Expression.Convert(argument, propertyInfo.PropertyType));
    //        return (Action<T, object>)Expression.Lambda(setterCall, instance, argument).Compile();
    //    }
    //}

    public sealed class TrackingConfiguration
    {
        //cache of type data for each type/context pair
        private static readonly Dictionary<Tuple<Type, string>, TypeTrackingMetaData> _typeMetadataCache =
            new Dictionary<Tuple<Type, string>, TypeTrackingMetaData>();

        private readonly SettingsTracker _tracker;

        private bool _applied;

        internal TrackingConfiguration(object target, SettingsTracker tracker)
        {
            _tracker = tracker;
            TargetReference = new WeakReference(target);
            Properties = new HashSet<string>();
            AddMetaData();

            var trackingAwareTarget = target as ITrackingAware;
            if (trackingAwareTarget != null)
                trackingAwareTarget.InitTracking(this);

            var asNotify = target as IRaiseTrackingNotifier;
            if (asNotify != null)
                asNotify.SettingsPersistRequest += (s, e) => Persist();
        }

        public string Key { get; set; }
        public HashSet<string> Properties { get; set; }
        public WeakReference TargetReference { get; private set; }
        public PersistModes Mode { get; set; }

        public void Persist()
        {
            if (_applyingInProgress)
                return;

            if (TargetReference.IsAlive && OnPersistingState())
            {
                foreach (var propertyName in Properties)
                {
                    var property = TargetReference.Target.GetType().GetProperty(propertyName);

                    var propKey = ConstructPropertyKey(property.Name);
                    try
                    {
                        var currentValue = property.GetValue(TargetReference.Target, null);
                        _tracker.ObjectStore.Persist(currentValue, propKey);
                    }
                    catch
                    {
                        Debug.WriteLine("Persisting of value '{propKey}' failed!");
                    }
                }

                OnPersistedState();
            }
        }

        public void Apply()
        {
            DoApply(true);
        }

        internal void JustApply()
        {
            DoApply(false);
        }

        private bool _applyingInProgress;

        private void DoApply(bool withEvents)
        {
            _applyingInProgress = true;
            var sw = new Stopwatch();

            if (TargetReference.IsAlive && OnApplyingState(withEvents))
            {
                foreach (var propertyName in Properties)
                {
                    sw.Restart();
                    var property = TargetReference.Target.GetType().GetProperty(propertyName);
                    var propKey = ConstructPropertyKey(property.Name);
                    try
                    {
                        if (_tracker.ObjectStore.ContainsKey(propKey))
                        {
                            var storedValue = _tracker.ObjectStore.Retrieve(propKey);
                            property.SetValue(TargetReference.Target, storedValue, null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Trace.WriteLine(string.Format("TRACKING: Applying tracking to property with key='{0}' failed. ExceptionType:'{1}', message: '{2}'!",
                            propKey, ex.GetType().Name, ex.Message));
                    }
                }

                OnAppliedState(withEvents);
            }
            _applied = true;
            _applyingInProgress = false;
        }

        public TrackingConfiguration AddProperties(params string[] properties)
        {
            foreach (var property in properties)
                Properties.Add(property);
            return this;
        }

        public TrackingConfiguration AddProperties<T>(params Expression<Func<T, object>>[] properties)
        {
            AddProperties(properties.Select(p => GetPropertyNameFromExpression(p)).ToArray());
            return this;
        }

        public TrackingConfiguration RemoveProperties(params string[] properties)
        {
            foreach (var property in properties)
                Properties.Remove(property);
            return this;
        }

        public TrackingConfiguration RemoveProperties<T>(params Expression<Func<T, object>>[] properties)
        {
            RemoveProperties(properties.Select(p => GetPropertyNameFromExpression(p)).ToArray());
            return this;
        }

        public TrackingConfiguration RegisterPersistTrigger(string eventName)
        {
            return RegisterPersistTrigger(eventName, TargetReference.Target);
        }

        public TrackingConfiguration RegisterPersistTrigger(string eventName, object eventSourceObject)
        {
            Mode = PersistModes.Manual;

            var eventInfo = eventSourceObject.GetType().GetEvent(eventName);
            var parameters = eventInfo.EventHandlerType
                .GetMethod("Invoke")
                .GetParameters()
                .Select(parameter => Expression.Parameter(parameter.ParameterType))
                .ToArray();

            var handler = Expression.Lambda(
                eventInfo.EventHandlerType,
                Expression.Call(Expression.Constant(new Action(() =>
                {
                    if (_applied)
                        Persist();
                })), "Invoke", Type.EmptyTypes),
                parameters)
                .Compile();

            eventInfo.AddEventHandler(eventSourceObject, handler);
            return this;
        }

        public TrackingConfiguration SetMode(PersistModes mode)
        {
            Mode = mode;
            return this;
        }

        public TrackingConfiguration SetKey(string key)
        {
            Key = key;
            return this;
        }

        private TrackingConfiguration AddMetaData()
        {
            var t = TargetReference.Target.GetType();
            var metadata = GetTypeData(t, _tracker != null ? _tracker.Name : null);

            if (!string.IsNullOrEmpty(metadata.KeyPropertyName))
                Key = t.GetProperty(metadata.KeyPropertyName).GetValue(TargetReference.Target, null).ToString();
            foreach (var propName in metadata.PropertyNames)
                Properties.Add(propName);
            return this;
        }

        private static TypeTrackingMetaData GetTypeData(Type t, string trackerName)
        {
            var _key = new Tuple<Type, string>(t, trackerName);
            if (!_typeMetadataCache.ContainsKey(_key))
            {
                var keyProperty = t.GetProperties().SingleOrDefault(pi => pi.IsDefined(typeof (TrackingKeyAttribute), true));

                //see if TrackableAttribute(true) exists on the target class
                var isClassMarkedAsTrackable = false;
                var targetClassTrackableAtt =
                    t.GetCustomAttributes(true).OfType<TrackableAttribute>().FirstOrDefault(ta => ta.TrackerName == trackerName);
                if (targetClassTrackableAtt != null && targetClassTrackableAtt.IsTrackable)
                    isClassMarkedAsTrackable = true;

                //add properties that need to be tracked
                var properties = new List<string>();
                foreach (var pi in t.GetProperties())
                {
                    //don't track the key property
                    if (pi == keyProperty)
                        continue;

                    var propTrackableAtt = pi.GetCustomAttributes(true).OfType<TrackableAttribute>().FirstOrDefault(ta => ta.TrackerName == trackerName);
                    if (propTrackableAtt == null)
                    {
                        //if the property is not marked with Trackable(true), check if the class is
                        if (isClassMarkedAsTrackable)
                            properties.Add(pi.Name);
                    }
                    else
                    {
                        if (propTrackableAtt.IsTrackable)
                            properties.Add(pi.Name);
                    }
                }

                string keyName = null;
                if (keyProperty != null)
                    keyName = keyProperty.Name;
                _typeMetadataCache[_key] = new TypeTrackingMetaData(trackerName, keyName, properties);
            }
            return _typeMetadataCache[_key];
        }

        private static string GetPropertyNameFromExpression<T>(Expression<Func<T, object>> exp)
        {
            MemberExpression membershipExpression;
            if (exp.Body is UnaryExpression)
                membershipExpression = (exp.Body as UnaryExpression).Operand as MemberExpression;
            else
                membershipExpression = exp.Body as MemberExpression;
            return membershipExpression.Member.Name;
        }

        private string ConstructPropertyKey(string propertyName)
        {
            return string.Format("{0}_{1}.{2}", TargetReference.Target.GetType().Name, Key, propertyName);
        }

        private sealed class TypeTrackingMetaData
        {
            public TypeTrackingMetaData(string context, string keyPropertyName, IEnumerable<string> propertyNames)
            {
                Context = context;
                KeyPropertyName = keyPropertyName;
                PropertyNames = propertyNames;
            }

            public string Context { get; private set; }
            public string KeyPropertyName { get; private set; }
            public IEnumerable<string> PropertyNames { get; private set; }
        }

        #region apply/persist events

        public event EventHandler<TrackingOperationEventArgs> ApplyingState;

        private bool OnApplyingState(bool withEvents)
        {
            if (withEvents && ApplyingState != null)
            {
                var args = new TrackingOperationEventArgs(this);
                ApplyingState(this, args);
                return !args.Cancel;
            }
            return true;
        }

        public event EventHandler AppliedState;

        private void OnAppliedState(bool withEvents)
        {
            if (withEvents && AppliedState != null)
                AppliedState(this, EventArgs.Empty);
        }

        public event EventHandler<TrackingOperationEventArgs> PersistingState;

        private bool OnPersistingState()
        {
            if (PersistingState != null)
            {
                var args = new TrackingOperationEventArgs(this);
                PersistingState(this, args);
                return !args.Cancel;
            }
            return true;
        }

        public event EventHandler PersistedState;

        private void OnPersistedState()
        {
            if (PersistedState != null)
                PersistedState(this, EventArgs.Empty);
        }

        #endregion
    }
}