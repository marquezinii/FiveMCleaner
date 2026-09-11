namespace Ralven.Contracts;

/// <summary>Signals that the signed broker manifest failed integrity verification.</summary>
public sealed class BrokerIntegrityException(Exception innerException)
    : Exception("Broker integrity verification failed.", innerException);
