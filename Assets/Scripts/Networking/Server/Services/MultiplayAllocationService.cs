using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Matchmaker.Models;
using UnityEngine;

public class MultiplayAllocationService : IDisposable
{
    private CancellationTokenSource serverCheckCancel;
    string allocationId;

    public MultiplayAllocationService()
    {
        try
        {
            serverCheckCancel = new CancellationTokenSource();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Error creating Multiplay allocation service.\n{ex}");
        }
    }

    public async Task<MatchmakingResults> SubscribeAndAwaitMatchmakerAllocation()
    {
        allocationId = null;
        return null;
    }

    public async Task BeginServerCheck()
    {
        await Task.CompletedTask;
    }

    public void SetServerName(string name)
    {
    }
    public void SetBuildID(string id)
    {
    }

    public void SetMaxPlayers(ushort players)
    {
    }

    public void AddPlayer()
    {
    }

    public void RemovePlayer()
    {
    }

    public void SetMap(string newMap)
    {
    }

    public void SetMode(string mode)
    {
    }

    public void Dispose()
    {
        if (serverCheckCancel != null)
        {
            serverCheckCancel.Cancel();
            serverCheckCancel.Dispose();
        }
    }
}