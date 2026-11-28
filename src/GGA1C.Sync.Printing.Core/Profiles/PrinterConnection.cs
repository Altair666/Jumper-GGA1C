using System.Buffers.Binary;
using System.Globalization;
using System.Net;

namespace GGA1C.Sync.Printing.Profiles;

public sealed record PrinterConnection(string IPv4 = "", int Port = PrinterConnection.DefaultPort,
    string Type = PrinterConnection.Network, bool ImportDecided = false)
{
    public const string Network = "Network";
    public const int DefaultPort = 9100;
    public const int MinPort = 1;
    public const int MaxPort = 65535;
    public const string AddressError = "Введите IPv4: четыре числа от 0 до 255 через точку, без ведущих нулей.";
    public const string PortError = "Порт должен быть целым числом от 1 до 65535.";

    public static bool TryIPv4(string? text, out IPAddress address)
    {
        address = IPAddress.None;
        if (text is null || text.Length is < 7 or > 15) return false;
        var parts = text.Split('.');
        Span<byte> bytes = stackalloc byte[4];
        if (parts.Length != bytes.Length) return false;
        for (var i = 0; i < bytes.Length; i++)
            if (parts[i].Length == 0 || (parts[i].Length > 1 && parts[i][0] == '0') ||
                !parts[i].All(char.IsAsciiDigit) ||
                !byte.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i])) return false;
        address = new IPAddress(bytes);
        return true;
    }

    // GoLabel II PrinterSetup.IP2Long: a.b.c.d -> a + b*256 + c*65536 + d*16777216.
    public static uint EncodeIPv4(string address) => address.Length == 0 ? 0 :
        TryIPv4(address, out var ip) ? BinaryPrimitives.ReadUInt32LittleEndian(ip.GetAddressBytes()) :
        throw new ArgumentException(AddressError);

    public static string DecodeIPv4(uint value)
    {
        if (value == 0) return "";
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return new IPAddress(bytes).ToString();
    }

    public static bool ValidPort(int port) => port is >= MinPort and <= MaxPort;

    public void Validate(bool requireAddress = false)
    {
        if (Type != Network) throw new ArgumentException("Поддерживается только LAN.");
        if ((requireAddress || IPv4 != "") && !TryIPv4(IPv4, out _))
            throw new ArgumentException(IPv4 == "" ? "Сначала укажите IPv4 принтера." : AddressError);
        if (!ValidPort(Port)) throw new ArgumentException(PortError);
    }
}
