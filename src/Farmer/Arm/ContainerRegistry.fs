[<AutoOpen>]
module Farmer.Arm.ContainerRegistry

open Farmer
open Farmer.ContainerRegistry
open Farmer.CoreTypes

let registries = ResourceType ("Microsoft.ContainerRegistry/registries", "2019-05-01")

type Registries =
    { Name : ResourceName
      Location : Location
      Sku : Sku
      AdminUserEnabled : bool
      Tags: Map<string,string> }
    interface IArmResource with
        member this.ResourceName = this.Name
        member this.JsonModel =
            {| name = this.Name.Value
               ``type`` = registries.Path
               apiVersion = registries.Version
               sku = {| name = this.Sku.ToString() |}
               location = this.Location.ArmValue
               tags = this.Tags
               properties = {| adminUserEnabled = this.AdminUserEnabled |}
            |} :> _