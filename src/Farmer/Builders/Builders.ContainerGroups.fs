[<AutoOpen>]
module Farmer.Builders.ContainerGroups

open Farmer
open Farmer.ContainerGroup
open Farmer.Arm.ContainerInstance
open Farmer.Arm.Network

/// Represents configuration for a single Container.
type ContainerInstanceConfig =
    { /// The name of the container instance
      Name : ResourceName
      /// The container instance image
      Image : string
      /// List of ports the container instance listens on
      Ports : uint16 list
      /// Max number of CPU cores the container instance may use
      Cpu : int
      /// Max gigabytes of memory the container instance may use
      Memory : float<Gb> }

type ContainerGroupConfig =
    { /// The name of the container group.
      Name : ResourceName
      /// Container group OS.
      OperatingSystem : OS
      /// Restart policy for the container group.
      RestartPolicy : RestartPolicy
      /// IP address for the container group.
      IpAddress : ContainerGroupIpAddress
      /// Name of the network profile for this container's group.
      NetworkProfile : ResourceName option
      /// The instances in this container group.
      Instances : ContainerInstanceConfig list }
    interface IBuilder with
        member this.DependencyName = this.Name
        member this.BuildResources location existingResources = [
            { Location = location
              Name = this.Name
              ContainerInstances = [
                  for instance in this.Instances do
                    {| Name = instance.Name
                       Image = instance.Image
                       Ports = instance.Ports
                       Cpu = instance.Cpu
                       Memory = instance.Memory |} ]
              OperatingSystem = this.OperatingSystem
              RestartPolicy = this.RestartPolicy
              IpAddress = this.IpAddress
              NetworkProfile = this.NetworkProfile }
        ]

type ContainerGroupBuilder() =
    member __.Yield _ =
      { Name = ResourceName.Empty
        OperatingSystem = Linux
        RestartPolicy = AlwaysRestart
        IpAddress = { Type = PublicAddress; Ports = Set.empty }
        NetworkProfile = None
        Instances = [] }
    member this.Run (state:ContainerGroupConfig) =
        state.Instances
        |> List.collect(fun i -> i.Ports)
        |> List.fold (fun (state:ContainerGroupConfig) port ->
            { state with IpAddress = { state.IpAddress with Ports = state.IpAddress.Ports.Add {| Protocol = TCP; Port = port |} } }) state

    member __.AddTcpPort(state:ContainerGroupConfig, port) = { state with IpAddress = { state.IpAddress with Ports = state.IpAddress.Ports.Add {| Protocol = TCP; Port = port |} } }

    [<CustomOperation "name">]
    /// Sets the name of the container group.
    member __.Name(state:ContainerGroupConfig, name) = { state with Name = name }
    member this.Name(state:ContainerGroupConfig, name) = this.Name(state, ResourceName name)
    /// Sets the OS type (default Linux)
    [<CustomOperation "operating_system">]
    member __.OsType(state:ContainerGroupConfig, os) = { state with OperatingSystem = os }
    /// Sets the restart policy (default Always)
    [<CustomOperation "restart_policy">]
    member __.RestartPolicy(state:ContainerGroupConfig, restartPolicy) = { state with RestartPolicy = restartPolicy }
    member private _.SetIpAddress(state:ContainerGroupConfig, ipAddressType, ports) =
      { state with
          IpAddress =
            { Type = ipAddressType
              Ports = ports |> Seq.map(fun (prot, port) -> {| Protocol = prot; Port = port |}) |> Set } }

    /// Sets the IP addresss to a public address with a DNS label
    [<CustomOperation "public_dns">]
    member this.PublicDns(state, dnsLabel, ports) = this.SetIpAddress(state, PublicAddressWithDns dnsLabel, ports)
    /// Sets the IP addresss to a private address that is statically assigned
    [<CustomOperation "private_static_ip">]
    member this.PrivateStaticIp(state, ip, ports) = this.SetIpAddress(state, PrivateAddressWithIp (System.Net.IPAddress.Parse ip), ports)
    /// Sets the IP addresss to a private address assigned by the vnet
    [<CustomOperation "private_ip">]
    member this.PrivateIp(state:ContainerGroupConfig, ports) = this.SetIpAddress(state, PrivateAddress, ports)
    /// Sets a network profile for the container's group.
    [<CustomOperation "network_profile">]
    member __.NetworkProfile(state:ContainerGroupConfig, networkProfileName:string) = { state with NetworkProfile = Some (ResourceName networkProfileName) }
    /// Adds a UDP port to be externally accessible
    [<CustomOperation "add_udp_port">]
    member __.AddUdpPort(state:ContainerGroupConfig, port) = { state with IpAddress = { state.IpAddress with Ports = state.IpAddress.Ports.Add {| Protocol = UDP; Port = port |} } }
    /// Adds a collection of container instances to this group
    [<CustomOperation "add_instances">]
    member __.AddInstances(state:ContainerGroupConfig, instances) = { state with Instances = state.Instances @ (Seq.toList instances) }

type ContainerInstanceBuilder() =
    member __.Yield _ =
      { Name = ResourceName.Empty
        Image = ""
        Ports = []
        Cpu = 1
        Memory = 1.5<Gb> }
    /// Sets the name of the container instance.
    [<CustomOperation "name">]
    member __.Name(state:ContainerInstanceConfig, name) = { state with Name = name }
    member this.Name(state:ContainerInstanceConfig, name) = this.Name(state, ResourceName name)
    /// Sets the image of the container instance.
    [<CustomOperation "image">]
    member __.Image (state:ContainerInstanceConfig, image) = { state with Image = image }
    /// Sets the ports the container instance exposes. These will automatically be applied to the container group.
    [<CustomOperation "ports">]
    member __.Ports (state:ContainerInstanceConfig, ports) = { state with Ports = ports }
    /// Sets the maximum CPU cores the container instance may use
    [<CustomOperationAttribute "cpu_cores">]
    member __.CpuCount (state:ContainerInstanceConfig, cpuCount) = { state with Cpu = cpuCount }
    /// Sets the maximum gigabytes of memory the container instance may use
    [<CustomOperationAttribute "memory">]
    member __.Memory (state:ContainerInstanceConfig, memory) = { state with Memory = memory }

let containerGroup = ContainerGroupBuilder()
let containerInstance = ContainerInstanceBuilder()

type ContainerNetworkInterfaceIpConfig = { Subnet : string }
type ContainerNetworkInterfaceConfiguration = { IpConfigs : ContainerNetworkInterfaceIpConfig list }

type NetworkProfileConfig =
    { Name : ResourceName
      ContainerNetworkInterfaceConfigurations : ContainerNetworkInterfaceConfiguration list
      VirtualNetwork : ResourceName }
    interface IBuilder with
        member this.DependencyName = this.Name
        member this.BuildResources location _ = [
            { Name = this.Name
              Location = location
              ContainerNetworkInterfaceConfigurations =
                  this.ContainerNetworkInterfaceConfigurations
                  |> List.map (fun ifconfig -> {| IpConfigs = (ifconfig.IpConfigs |> List.map (fun ipConfig -> {| SubnetName = ResourceName ipConfig.Subnet |})) |})
              VirtualNetwork = this.VirtualNetwork
            }
        ]

type NetworkProfileBuilder() =
    member __.Yield _ =
      { Name = ResourceName.Empty
        ContainerNetworkInterfaceConfigurations = []
        VirtualNetwork = ResourceName.Empty }
    /// Sets the name of the network profile instance
    [<CustomOperation "name">]
    member __.Name(state:NetworkProfileConfig, name) = { state with Name = ResourceName name }
    /// Sets a single target subnet for the network profile (typical case of single subnet)
    [<CustomOperation "subnet">]
    member __.SubnetName(state:NetworkProfileConfig, subnet) = { state with ContainerNetworkInterfaceConfigurations = [ { IpConfigs = [ { Subnet = subnet } ] } ] }
    /// Sets a single target subnet for the network profile (typical case of single subnet)
    [<CustomOperation "add_ip_configs">]
    member __.AddIpConfigs(state:NetworkProfileConfig, configs) = { state with ContainerNetworkInterfaceConfigurations = state.ContainerNetworkInterfaceConfigurations @ configs }
    /// Sets the virtual network for the profile
    [<CustomOperation "vnet">]
    member __.VirtualNetwork(state:NetworkProfileConfig, vnet) = { state with VirtualNetwork = ResourceName vnet }

let networkProfile = NetworkProfileBuilder ()