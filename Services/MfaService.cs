using OtpNet;
using QRCoder;

namespace BloomCast.Services
{
    public interface IMfaService
    {
        string GenerarSecretKey();
        string GenerarQrCode(string email, string secret);
        bool ValidarCodigo(string secret, string codigo);
    }

    public class MfaService : IMfaService
    {
        private const string Issuer = "BloomCast";

        public string GenerarSecretKey()
        {
            var key = KeyGeneration.GenerateRandomKey(20);
            return Base32Encoding.ToString(key);
        }

        public string GenerarQrCode(string email, string secret)
        {
            var otpUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(email)}" +
                         $"?secret={secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

            var qrGenerator = new QRCodeGenerator();
            var qrData = qrGenerator.CreateQrCode(otpUri, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrData);
            var qrBytes = qrCode.GetGraphic(10);
            return Convert.ToBase64String(qrBytes);
        }

        public bool ValidarCodigo(string secret, string codigo)
        {
            try
            {
                var secretBytes = Base32Encoding.ToBytes(secret);
                var totp = new Totp(secretBytes);
                return totp.VerifyTotp(codigo, out _, new VerificationWindow(2, 2));
            }
            catch
            {
                return false;
            }
        }
    }
}
