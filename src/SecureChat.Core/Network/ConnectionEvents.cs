using SecureChat.Core.Protocol;

namespace SecureChat.Core.Network;

// Uspesno primljena i desifrovana txt ili control poruka
public sealed record ReceivedMessage(MessageHeader Header, string Text);

// Poruka koja nije prosla proveru (integritet/desiforvanje). Veza i dalje tu 
public sealed record ReceiveFailure(MessageHeader Header, Exception Error);

// Veza je zavrsena. Error - null kod uredno zatvorene veze
public sealed record DisconnectInfo(string Reason, Exception? Error);

// Primanje, proveravanje, desifrovanje i cuvanje file'a
public sealed record ReceivedFile(MessageHeader Header, string SavedPath);

// Akko sifrat ceo procitan a file failed
public sealed class RecoverableReceiveException(string message, Exception? inner = null) : Exception(message, inner);

// Prima file, mora da potrosi tacno velicinu encrypted headera (B) iz toka, cak i kada file odbije.
// Inace otpada sinhronizacija sa porukom. Ako greska ne kvari tok - RRE, sve ostalo prekida vezu
public delegate Task<string> FileReceiveHandler(MessageHeader Header, Stream payload, CancellationToken ct);

// Poslata Text ili Control poruka. Tek nakon uspesnog slanja
public sealed record SentMessage(MessageHeader Header, string Text);