namespace Waylonia.Sessions;

internal interface ISshLinkFactory
{
    ISshLink Create(string destination, ISshPrompter prompter);
}
