Service Registry
================

A simple dependency injection helper for .Net.

Service Registry allows you to more loosely-couple your applications by binding abstract or interface definitions of your components to their actual concrete implementations by configuration. It is an implementation of the [Inversion of Control](http://martinfowler.com/articles/injection.html) (or Dependency Injection) pattern.

#### Features

  - Lightweight: only a single (public) class required, just over 500 lines of code
  - Initialize from config (XML) or in code
  - Support for singletons
  - Support for detailed logging with ETW Events
  - Can call non-default constructors on types.

#### Terminology

If you have an interface, `IComponent` which a type `SomeComponent` implements, then you can **Bind** `SomeComponent` as the **Concrete** implementation for **Abstract** type, `IComponent`. Later, you can **Get** an instance of `SomeComponent` using the Service Registry.

Get it
------

Install from [NuGet](https://www.nuget.org/packages/willpe.ServiceRegistry):

    PM> Install-Package willpe.ServiceRegistry

Or [Download](https://github.com/willpe/ServiceRegistry/releases/latest) the latest release as a zip file.

Use it
------

Take a look at the samples in `Example.cs`.

The most straightforward way to get started it to bind an **abstract** type to its **concrete** implementation in code, like this:

````csharp
// Bind a type in code
ServiceRegistry.Instance.Bind<ICollection<int>, List<int>>();
var ints = ServiceRegistry.Instance.Get<ICollection<int>>();
Console.WriteLine("Ints is a {0}", ints.GetType().Name);
````

This (somewhat trivial) example essentially says *'When somebody needs an ICollection of ints, give them a List<int>'*.

#### Using Configuration 

You can also setup bindings from a configuration file. Service Registry accepts an XmlElement that contains configuration information. You can get this from an existing config file, or use a standalone file like the example `config.xml` included in the repo:

````xml
<?xml version="1.0" encoding="utf-8" ?>

<!-- Service Registry Configuration
         Configuration for a simple dependency injection, ServiceRegistry -->
<serviceRegistry>
  <bindings>

    <clear />

    <add abstract="System.IO.Stream"
         concrete="System.IO.MemoryStream" />

  </bindings>
</serviceRegistry>

````

From code, simply load up the config file, grab the correct Xml Element and pass it to the `Init` method:

````csharp
var config = new XmlDocument();
config.Load("config.xml");
var serviceRegistryConfigElement = config.DocumentElement;

// Initialize from configuration xml
ServiceRegistry.Instance.Init(serviceRegistryConfigElement);
Console.WriteLine("Loaded {0} bindings from config", ServiceRegistry.Instance.Bindings.Count());

// Get an object from the registry
var stream = ServiceRegistry.Instance.Get<Stream>();
Console.WriteLine("Stream is a {0}", stream.GetType().Name);
stream.Dispose();
````

#### Using Singletons

If you bind an abstract type to an **instance** of an object, then that instance will be returned whenever `Get` is called for that abstract type. As such, the instance becomes a **singleton**. 

You can bind a singleton in code by calling `Bind` and passing in an instance of an object:

````csharp
var ints = new List<int>();

// Bind to a singleton
ServiceRegistry.Instance.Bind<ICollection<int>>(ints);
            
var intsRef2 = ServiceRegistry.Instance.Get<ICollection<int>>();
Console.WriteLine("Is same as ints? {0}", object.ReferenceEquals(ints, intsRef2));
````

If you prefer a config-driven approach, you can also specify singleton behavior in a config file using the `isSingleton` attribute:
````xml
    <add abstract="System.IO.Stream"
         concrete="System.IO.MemoryStream"
         isSingleton="true" />
````

#### Using a non-default constructor

When a type is bound, you can pass arguments into the `Get` method to invoke a non-default constructor on the concrete implementing type:

````csharp
// Create with constructor arguments
ServiceRegistry.Instance.Bind<DirectoryInfo, DirectoryInfo>();
var dirInfo = ServiceRegistry.Instance.Get<DirectoryInfo>("c:\\");
Console.WriteLine("dirInfo.FullName: {0}", dirInfo.FullName);
````

#### Debugging and Logging

The Service Registry exposes an [EventSource](http://msdn.microsoft.com/en-us/library/system.diagnostics.tracing.eventsource.aspx) which can be used to log debugging and tracing information. You can hook this up to a variety of built-in event listeners or extend the EventListener class to create your own.

````csharp
    // Capture events from an Event Source
    var consoleListener = new ConsoleEventListener();
    consoleListener.EnableEvents(ServiceRegistry.Instance.Log, EventLevel.LogAlways);

    ServiceRegistry.Instance.Bind<ICollection<int>, HashSet<int>>();

}

// Trivial example of an Event Listener
private class ConsoleEventListener : EventListener
{
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        Console.WriteLine("{0}\r\nEvent {1}: {2}", eventData.EventSource, eventData.EventId, string.Format(eventData.Message, eventData.Payload.ToArray()));
    }
}
````


## Revision History

  - 1.1.0: Added `TryGet(...)` and `Find(...)` methods to more easily allow consumers to fallback if bindings are not found