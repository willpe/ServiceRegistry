// ------------------------------------------------------------------------------------------------
// <copyright file="ServiceRegistry.cs" company="Microsoft">
//   Copyright 2013 Will Perry, Microsoft
//   
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//   
//       http://www.apache.org/licenses/LICENSE-2.0
//   
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
// </copyright>
// ------------------------------------------------------------------------------------------------
namespace WillPe.Utils
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.Tracing;
    using System.Linq;
    using System.Xml;

    /// <summary>
    /// A simple inversion of control helper
    /// </summary>
    public sealed class ServiceRegistry : IDisposable
    {
        private static readonly ServiceRegistry instance = new ServiceRegistry();

        private readonly IDictionary<Type, Binding> bindings = new Dictionary<Type, Binding>();

        private readonly ServiceRegistryEventSource log;

        private readonly object syncRoot = new object();

        private bool disposed;

        private ServiceRegistry()
        {
            this.log = new ServiceRegistryEventSource();
        }

        ~ServiceRegistry()
        {
            this.Dispose(false);
        }

        public static ServiceRegistry Instance
        {
            get { return ServiceRegistry.instance; }
        }

        public IDictionary<Type, Type> Bindings
        {
            get { return this.bindings.ToDictionary(b => b.Key, b => b.Value.ConcreteType); }
        }

        public EventSource Log
        {
            get { return this.log; }
        }

        /// <summary>
        /// Registers <typeparamref name="TConcrete"/> as the default implementation
        /// of <typeparamref name="TAbstract"/>
        /// </summary>
        /// <typeparam name="TAbstract">The abstract type or interface.</typeparam>
        /// <typeparam name="TConcrete">The concrete type.</typeparam>
        public void Bind<TAbstract, TConcrete>() where TConcrete : class, TAbstract
        {
            this.Bind(typeof(TAbstract), typeof(TConcrete));
        }

        /// <summary>
        /// Registers <paramref name="concreteType"/> as the default implementation
        /// of <paramref name="abstractType"/>.
        /// </summary>
        /// <param name="abstractType">The abstract type or interface.</param>
        /// <param name="concreteType">The concrete type.</param>
        public void Bind(Type abstractType, Type concreteType)
        {
            if (abstractType == null)
            {
                throw new ArgumentNullException("abstractType");
            }

            if (concreteType == null)
            {
                throw new ArgumentNullException("concreteType");
            }

            this.AddOrUpdateBinding(abstractType, concreteType);
        }

        /// <summary>
        /// Registers <paramref name="singleton"/> as the singleton implementation
        /// of <typeparamref name="TAbstract"/>.
        /// </summary>
        /// <typeparam name="TAbstract">The abstract type or interface.</typeparam>
        /// <param name="singleton">The singleton.</param>
        public void Bind<TAbstract>(TAbstract singleton)
        {
            this.Bind(typeof(TAbstract), singleton);
        }

        /// <summary>
        /// Registers <paramref name="singleton"/> as the singleton implementation
        /// of <paramref name="abstractType"/>.
        /// </summary>
        /// <param name="abstractType">The abstract type or interface.</param>
        /// <param name="singleton">The singleton.</param>
        public void Bind(Type abstractType, object singleton)
        {
            if (abstractType == null)
            {
                throw new ArgumentNullException("abstractType");
            }

            if (singleton == null)
            {
                throw new ArgumentNullException("singleton");
            }

            this.AddOrUpdateBinding(abstractType, singleton);
        }

        /// <summary>
        /// Discards any bindings from the Service Registry
        /// </summary>
        public void Clear()
        {
            this.log.Clear(this.bindings.Count);

            lock (this.syncRoot)
            {
                this.bindings.Clear();
            }
        }

        public void Dispose()
        {
            this.Dispose(true);

            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Gets or creates an instance of the concrete class registered for <typeparamref name="TAbstract"/>.
        /// </summary>
        public TAbstract Get<TAbstract>(params object[] args)
        {
            return (TAbstract)this.Get(typeof(TAbstract), args);
        }

        /// <summary>
        /// Gets or creates an instance of the concrete class registered for abstractType.
        /// </summary>
        public object Get(Type abstractType, params object[] args)
        {
            var argTypes = new Type[0];
            if (args != null && args.Length > 0)
            {
                argTypes = args.Select(a => a.GetType()).ToArray();
            }

            return this.Get(abstractType, argTypes, args);
        }

        public void Init(XmlElement configuration)
        {
            this.log.Initializing();

            // Sample configuration:
            // <serviceRegistry>
            // <bindings>
            // <clear />
            // <add abstract="Microsoft.Test.ServiceBus.Push.Service.Hosting.HostingEnvironment, Microsoft.Test.ServiceBus.Push.Service"
            // concrete="Microsoft.Test.ServiceBus.Push.Service.AzureHost.AzureHostingEnvironment, Microsoft.Test.ServiceBus.Push.Service.AzureHost" />
            // <add abstract="Microsoft.Test.ServiceBus.Push.Service.Hosting.HostingContainerFactory, Microsoft.Test.ServiceBus.Push.Service"
            // concrete="Microsoft.Test.ServiceBus.Push.Service.Hosting.HostingContainerFactory, Microsoft.Test.ServiceBus.Push.Service" />
            // </bindings>
            // </serviceRegistry>
            if (configuration == null || configuration.LocalName != "serviceRegistry")
            {
                this.log.InvalidConfigurationFragment();
                throw new ArgumentException("The specififed configuration must be an element named serviceRegistry", "configuration");
            }

            var bindingConfigurations = configuration.SelectSingleNode("bindings");
            if (bindingConfigurations != null)
            {
                foreach (var bindingConfiguration in bindingConfigurations.OfType<XmlElement>())
                {
                    switch (bindingConfiguration.LocalName.ToLowerInvariant())
                    {
                        case "clear":
                            this.Clear();
                            break;

                        case "add":
                            this.AddBindingFromConfig(bindingConfiguration);
                            break;

                        default:
                            throw new NotSupportedException("The binding directive '" + bindingConfiguration.LocalName + "' is unknown.");
                    }
                }
            }
        }

        /// <summary>
        /// Reloads the Object Factory's configuration from app.config, discarding any bindings
        /// which have been programatically set.
        /// </summary>
        public void Reset(XmlElement configuration = null)
        {
            this.Clear();

            if (configuration != null)
            {
                this.Init(configuration);
            }
        }

        /// <summary>
        /// Removes any registration for creating instances of <typeparamref name="TAbstract"/>
        /// </summary>
        /// <typeparam name="TAbstract">The abstract type or interface.</typeparam>
        public void Unbind<TAbstract>()
        {
            this.Unbind(typeof(TAbstract));
        }

        /// <summary>
        /// Removes any registration for creating instances of <paramref name="abstractType"/>
        /// </summary>
        /// <param name="abstractType">The abstract type or interface.</param>
        public void Unbind(Type abstractType)
        {
            this.RemoveBinding(abstractType);
        }

        private void AddBindingFromConfig(XmlElement bindingConfiguration)
        {
            var abstractTypeName = bindingConfiguration.GetAttribute("abstract");
            if (string.IsNullOrEmpty(abstractTypeName))
            {
                throw new InvalidOperationException("Invalid binding configuration. The 'abstract' attribute is required (Element: '" + bindingConfiguration.OuterXml + "')");
            }

            var concreteTypeName = bindingConfiguration.GetAttribute("concrete");
            if (string.IsNullOrEmpty(concreteTypeName))
            {
                throw new InvalidOperationException("Invalid binding configuration. The 'concrete' attribute is required (Element: '" + bindingConfiguration.OuterXml + "')");
            }

            var isSingletonAttribute = bindingConfiguration.GetAttribute("isSingleton");
            var isSingleton = false;
            if (!string.IsNullOrEmpty(isSingletonAttribute) && !bool.TryParse(isSingletonAttribute, out isSingleton))
            {
                throw new InvalidOperationException("Invalid binding configuration. If the optional 'isSingleton' is present, it must be 'true' or 'false' (Element: '" + bindingConfiguration.OuterXml + "')");
            }

            var abstractType = Type.GetType(abstractTypeName, true);
            var concreteType = Type.GetType(concreteTypeName, true);
            if (isSingleton)
            {
                var instance = Activator.CreateInstance(concreteType);
                this.AddOrUpdateBinding(abstractType, instance);
            }
            else
            {
                this.AddOrUpdateBinding(abstractType, concreteType);
            }
        }

        private void AddOrUpdateBinding(Type abstractType, Type concreteType)
        {
            this.log.SetBinding(abstractType.Name, concreteType.Name);

            Binding binding;
            lock (this.syncRoot)
            {
                if (!this.bindings.ContainsKey(abstractType))
                {
                    this.bindings.Add(abstractType, new Binding(abstractType));
                }

                binding = this.bindings[abstractType];
            }

            binding.ConcreteType = concreteType;
        }

        private void AddOrUpdateBinding(Type abstractType, object singleton)
        {
            this.log.SetSingletonBinding(abstractType.Name, singleton.GetType().Name);

            Binding binding;
            lock (this.syncRoot)
            {
                if (!this.bindings.ContainsKey(abstractType))
                {
                    this.bindings.Add(abstractType, new Binding(abstractType));
                }

                binding = this.bindings[abstractType];
            }

            binding.Singleton = singleton;
        }

        private void Dispose(bool disposing)
        {
            if (!this.disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here
                    this.log.Dispose();
                }

                this.disposed = true;
            }
        }

        private object Get(Type abstractType, Type[] constructorParameterTypes, object[] constructorParameters)
        {
            Binding binding;

            lock (this.syncRoot)
            {
                if (!this.bindings.ContainsKey(abstractType))
                {
                    this.log.BindingNotFound(abstractType.Name);
                    throw new ArgumentException("There is no binding for abstract type: " + abstractType.Name + ". Consider adding one in the <serviceRegistry> section of the configuration file, or using ServiceRegistry.Bind to specify an implementation");
                }

                binding = this.bindings[abstractType];
            }

            return binding.GetInstance(constructorParameterTypes, constructorParameters);
        }

        private void RemoveBinding(Type abstractType)
        {
            this.log.ClearBinding(abstractType.Name);

            lock (this.syncRoot)
            {
                if (this.bindings.ContainsKey(abstractType))
                {
                    this.bindings.Remove(abstractType);
                }
            }
        }

        private sealed class Binding
        {
            private readonly Type abstractType;

            private readonly object syncRoot;

            private Type concreteType;

            private object singleton;

            public Binding(Type abstractType)
            {
                if (abstractType == null)
                {
                    throw new ArgumentNullException("abstractType");
                }

                this.abstractType = abstractType;
                this.syncRoot = new object();
            }

            public Type AbstractType
            {
                get { return this.abstractType; }
            }

            public Type ConcreteType
            {
                get { return this.concreteType; }
                set { this.Bind(value); }
            }

            public bool IsSingleton { get; private set; }

            public object Singleton
            {
                get { return this.concreteType; }
                set { this.Bind(value); }
            }

            public void Bind(Type concrete)
            {
                if (concrete == null)
                {
                    throw new ArgumentNullException("concrete");
                }

                if (!this.abstractType.IsAssignableFrom(concrete))
                {
                    throw new ArgumentException(string.Format("{0} cannot be assigned to a variable of type {1}", concrete.Name, this.abstractType.Name));
                }

                lock (this.syncRoot)
                {
                    this.IsSingleton = false;
                    this.concreteType = concrete;
                    this.singleton = null;
                }
            }

            public void Bind(object singleton)
            {
                if (singleton == null)
                {
                    throw new ArgumentNullException("singleton");
                }

                if (!this.abstractType.IsAssignableFrom(singleton.GetType()))
                {
                    throw new ArgumentException(
                        string.Format("Object of type {0} cannot be assigned to a variable of type {1}", singleton.GetType().Name, this.abstractType.Name));
                }

                lock (this.syncRoot)
                {
                    this.IsSingleton = true;
                    this.concreteType = singleton.GetType();
                    this.singleton = singleton;
                }
            }

            public object GetInstance(Type[] constructorParameterTypes, object[] constructorParameters)
            {
                object singleton = null;
                Type concreteType = null;

                lock (this.syncRoot)
                {
                    if (this.IsSingleton)
                    {
                        singleton = this.singleton;
                    }
                    else
                    {
                        if (this.concreteType == null)
                        {
                            throw new InvalidOperationException(
                                string.Format("The service registry is not configured to create instances for abstract/interface type '{0}'", this.abstractType.Name));
                        }

                        concreteType = this.concreteType;
                    }
                }

                if (singleton != null)
                {
                    return singleton;
                }

                var constructor = concreteType.GetConstructor(constructorParameterTypes);
                if (constructor == null)
                {
                    var message = string.Format(
                        "An instance of type '{0}' cannot be created, because there is no constructor with parameter types ({1})",
                        this.concreteType.FullName,
                        string.Join(", ", constructorParameterTypes.Select(t => t.Name).ToArray()));

                    throw new InvalidOperationException(message);
                }

                return constructor.Invoke(constructorParameters);
            }
        }

        [EventSource(Name = "DaylightServiceRegistry")]
        private sealed class ServiceRegistryEventSource : EventSource
        {
            [Event(5, Message = "Cannot find a binding for requested abstract type '{0}'.", Level = EventLevel.Warning)]
            public void BindingNotFound(string abstractType)
            {
                this.WriteEvent(5, abstractType);
            }

            [Event(3, Message = "Clearing {0} binding(s)", Level = EventLevel.Informational)]
            public void Clear(int count)
            {
                this.WriteEvent(3, count);
            }

            [Event(4, Message = "Clearing binding for '{0}'.", Level = EventLevel.Informational)]
            public void ClearBinding(string abstractType)
            {
                this.WriteEvent(4, abstractType);
            }

            [Event(7, Message = "Initializing Service Registry from Xml Configuration", Level = EventLevel.Informational)]
            public void Initializing()
            {
                this.WriteEvent(7);
            }

            [Event(6, Message = "The specified configuration fragment is invalid", Level = EventLevel.Warning)]
            public void InvalidConfigurationFragment()
            {
                this.WriteEvent(6);
            }

            [Event(1, Message = "Binding '{0}' to '{1}'", Level = EventLevel.Informational)]
            public void SetBinding(string abstractType, string concreteType)
            {
                this.WriteEvent(1, abstractType, concreteType);
            }

            [Event(2, Message = "Binding '{0}' to a singleton instance of '{1}'", Level = EventLevel.Informational)]
            public void SetSingletonBinding(string abstractType, string concreteType)
            {
                this.WriteEvent(2, abstractType, concreteType);
            }
        }
    }
}