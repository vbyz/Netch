﻿using Netch.Models;

namespace Netch.Servers;

public class Socks5Server : Server
{
    public override string Type { get; } = "Socks5";

    /// <summary>
    ///     密码
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    ///     账号
    /// </summary>
    public string? Username { get; set; }

    public string? RemoteHostname { get; set; }

    public override string MaskedData()
    {
        return $"Auth: {Auth()}";
    }

    public Socks5Server()
    {
    }

    public Socks5Server(string hostname, ushort port)
    {
        Hostname = hostname;
        Port = port;
    }

    public Socks5Server(string hostname, ushort port, string username, string password) : this(hostname, port)
    {
        Username = username;
        Password = password;
    }

    public Socks5Server(string hostname, ushort port, string remoteHostname) : this(hostname, port)
    {
        RemoteHostname = remoteHostname;
    }

    public bool Auth()
    {
        return !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }
}