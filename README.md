# Example of using tracing with background/hosted service in .NET

This is an example of the sorts of things you can do with background processing in .NET

The example is a Web API that adds tasks to a queue, which a background service picks up.

There is the ability in the API call to tell it to use Span Links, or Parent/Child.
