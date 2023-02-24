# Building Locally

When experimenting with possible improvements, it can be useful to be able to build this project locally.

## Setup

You're going to need a fairly recent version of dotnet (for both basic functionality and accurate baselining).
One way to set that up is to download a [nightly build](https://github.com/dotnet/installer#table) and extract it into a local folder.
(You can also install it, since dotnet handles side-by-side installation, but this can get cluttered if you install a lot of nightlies.)
To use the nightly build you've extracted, you'll want to add the folder where you extracted it to the `PATH` and also set it as the `DOTNET_ROOT`.
On Windows, a batch file for setting these variables might look like

```
@echo off
set PATH=c:\dotnet;%PATH%
set DOTNET_ROOT=c:\dotnet\
```

In order to avoid interfering with your other uses of dotnet, it's probably preferable to set those variables only in the prompt where you're building this project.

Confirm that things are working by running `dotnet --version` - the output should match the build that you downloaded.
Tools like `which` and `where` can also help confirm that you're running the version you expect.

## Building

In a prompt with `PATH` and `DOTNET_ROOT` set appropriately, run the following command
```
dotnet publish /p:PublishAot=true /p:StripSymbols=true /p:EnableRequestDelegateGenerator=true
```
The properties and their values come from [goldilocks.benchmarks.yml](../../../scenarios/goldilocks.benchmarks.yml).

The build should complete without errors.

## Validating

Run `BasicMinimalApi` from the `publish` directory - something like `bin\Release\net8.0\win-x64\publish`.
You should be able to connect to the server at http://localhost:5000/todos.
(It will return a JSON blob.)

## Experimenting

As you make changes to your local `aspnetcore` build, you can pull those changes into the benchmark project by adding explicit references to the [csproj file](./BasicMinimalApi.csproj).
```xml
<ItemGroup>
  <Reference Include="e:\aspnetcore\artifacts\bin\Microsoft.AspNetCore\Release\net8.0\Microsoft.AspNetCore.dll" />
</ItemGroup>
```
