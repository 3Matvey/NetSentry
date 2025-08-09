namespace NetSentry.Crypto
{
    public interface ITunnelCryptoSession : IDisposable
    {
        // Шифрует frame -> (nonce, ciphertext, tag)
        void Encrypt(ReadOnlySpan<byte> frame, Span<byte> nonceOut, Span<byte> cipherOut, Span<byte> tagOut);

        // Дешифрует (nonce, ciphertext, tag) -> plaintext в plainOut; false если аутентификация не прошла
        bool Decrypt(ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> cipher, ReadOnlySpan<byte> tag, Span<byte> plainOut);
    }
}
