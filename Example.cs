// ------------------------------------------------------------------------------------------------
// <copyright file="Example.cs" company="Microsoft">
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
    using System.IO;
    using System.Linq;
    using System.Xml;

    internal class Program
    {
        private static void Main(string[] args)
        {
            var config = new XmlDocument();
            config.Load("config.xml");
            var serviceRegistryConfigElement = config.DocumentElement;

            // Initialize from configuration xml
            ServiceRegistry.Instance.Init(serviceRegistryConfigElement);
            Console.WriteLine("Loaded {0} bindings from config", ServiceRegistry.Instance.Bindings.Count());

            // Get an object from the factory
            var stream = ServiceRegistry.Instance.Get<Stream>();
            Console.WriteLine("Stream is a {0}", stream.GetType().Name);
            stream.Dispose();

            // Bind a type in code
            ServiceRegistry.Instance.Bind<ICollection<int>, List<int>>();
            var ints = ServiceRegistry.Instance.Get<ICollection<int>>();
            Console.WriteLine("Ints is a {0}", ints.GetType().Name);

            // Bind to a singleton
            ServiceRegistry.Instance.Bind<ICollection<int>>(ints);
            var intsRef2 = ServiceRegistry.Instance.Get<ICollection<int>>();
            Console.WriteLine("Is same as ints? {0}", object.ReferenceEquals(ints, intsRef2));

            // Create with constructor arguments
            ServiceRegistry.Instance.Bind<DirectoryInfo, DirectoryInfo>();
            var dirInfo = ServiceRegistry.Instance.Get<DirectoryInfo>("c:\\");
            Console.WriteLine("dirInfo.FullName: {0}", dirInfo.FullName);

            // Return null if there is no binding for the requested abstract type
            var nothing = ServiceRegistry.Find<FileInfo>();
            Console.WriteLine("fileInfo is {0}", nothing == null ? "null" : "not null");
            
            // Use the TryGet pattern to determine if a binding exists
            StreamReader reader
            if (ServiceRegistry.TryGet<StreamReader>(out reader))
            {
                Console.WriteLine("reader is a {0}", reader.GetType().Name);
            }
            else
            {
                Console.WriteLine("No binding for 'StreamReader'");
            }

            // Event Source
            var consoleListener = new ConsoleEventListener();
            consoleListener.EnableEvents(ServiceRegistry.Instance.Log, EventLevel.LogAlways);

            ServiceRegistry.Instance.Bind<ICollection<int>, HashSet<int>>();

            Console.WriteLine("Press <Enter> to exit");
            Console.ReadLine();
        }

        private class ConsoleEventListener : EventListener
        {
            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                Console.WriteLine("{0}\r\nEvent {1}: {2}", eventData.EventSource, eventData.EventId, string.Format(eventData.Message, eventData.Payload.ToArray()));
            }
        }
    }
}
