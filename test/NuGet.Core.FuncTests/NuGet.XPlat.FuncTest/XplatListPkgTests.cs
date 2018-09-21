// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.CommandLineUtils;
using Moq;
using NuGet.CommandLine.XPlat;
using NuGet.Packaging;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.XPlat.FuncTest
{
    [Collection("NuGet XPlat Test Collection")]
    public class XPlatListPkgTests
    {
        private static readonly string ProjectName = "test_project_listpkg";

        private static MSBuildAPIUtility MsBuild => new MSBuildAPIUtility(new TestCommandOutputLogger());

        [Fact]
        public async void DotnetListPackage_Succeed()
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46");

                var packageX = XPlatTestUtils.CreatePackage();

                // Generate Package
                await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);

                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --version 1.0.0 --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var restoreResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"restore {projectA.ProjectName}.csproj",
                    waitForExit: true);
                Assert.True(restoreResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package",
                    waitForExit: true);

                Assert.True(ContainsIgnoringSpaces(listResult.AllOutput, "packageX1.0.01.0.0"));

            }
        }

        [Fact]
        public async void DotnetListPackage_NoRestore_Fail()
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46");

                var packageX = XPlatTestUtils.CreatePackage();

                // Generate Package
                await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);


                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package",
                    waitForExit: true);

                Assert.True(ContainsIgnoringSpaces(listResult.AllOutput, "No assets file was found".Replace(" ", "")));
            }
        }

        [Fact]
        public async void DotnetListPackage_Transitive()
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46");

                var packageX = XPlatTestUtils.CreatePackage();
                var packageY = XPlatTestUtils.CreatePackage(packageId: "packageY");
                packageX.Dependencies.Add(packageY);

                // Generate Package
                await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX,
                    packageY);


                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var restoreResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"restore {projectA.ProjectName}.csproj",
                    waitForExit: true);
                Assert.True(restoreResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package",
                    waitForExit: true);

                Assert.False(ContainsIgnoringSpaces(listResult.AllOutput, "packageY"));

                listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package --include-transitive",
                    waitForExit: true);

                Assert.True(ContainsIgnoringSpaces(listResult.AllOutput, "packageY"));

            }
        }

        [Theory]
        [InlineData("", "net46", null)]
        [InlineData("", "net451", null)]
        [InlineData("--framework net451 --framework net46", "net46", null)]
        [InlineData("--framework net451 --framework net46", "net451", null)]
        [InlineData("--framework net451", "net451", "net46")]
        [InlineData("--framework net46", "net46", "net451")]
        public async void DotnetListPackage_FrameworkSpecific_Success(string args, string shouldInclude, string shouldntInclude)
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46;net451");

                var packageX = XPlatTestUtils.CreatePackage(frameworkString: "net46;net451");

                // Generate Package
                await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);


                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var restoreResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"restore {projectA.ProjectName}.csproj",
                    waitForExit: true);
                Assert.True(restoreResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package {args}",
                    waitForExit: true);

                Assert.True(ContainsIgnoringSpaces(listResult.AllOutput, shouldInclude.Replace(" ", "")));

                if (shouldntInclude != null)
                {
                    Assert.False(ContainsIgnoringSpaces(listResult.AllOutput, shouldntInclude.Replace(" ", "")));
                }

            }
        }

        [Fact]
        public async void DotnetListPackage_InvalidFramework_Fail()
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46");
                
                var packageX = XPlatTestUtils.CreatePackage();

                // Generate Package
                await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                    pathContext.PackageSource,
                    PackageSaveMode.Defaultv3,
                    packageX);


                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var restoreResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"restore {projectA.ProjectName}.csproj",
                    waitForExit: true);
                Assert.True(restoreResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package --framework net46 --framework invalidFramework",
                    waitForExit: true);

                Assert.False(listResult.Success);

            }
        }

        [Theory]
        [InlineData("1.0.0", "", "2.1.0")]
        [InlineData("1.0.0", "--include-prerelease", "2.2.0-beta")]
        [InlineData("1.0.0-beta", "", "2.2.0-beta")]
        [InlineData("1.0.0", "--highest-patch", "1.0.9")]
        [InlineData("1.0.0", "--highest-minor", "1.9.0")]
        [InlineData("1.0.0", "--highest-patch --include-prerelease", "1.0.10-beta")]
        [InlineData("1.0.0", "--highest-minor --include-prerelease", "1.10.0-beta")]
        public async void DotnetListPackage_Outdated_Succeed(string currentVersion, string args, string expectedVersion)
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                var projectA = XPlatTestUtils.CreateProject(ProjectName, pathContext, "net46");
                var versions = new List<string> { "1.0.0-beta", "1.0.0", "1.0.9", "1.0.10-beta", "1.9.0", "1.10.0-beta", "2.1.0", "2.2.0-beta" };
                foreach (var version in versions)
                {
                    var packageX = XPlatTestUtils.CreatePackage(packageId: "packageX", packageVersion: version);

                    // Generate Package
                    await SimpleTestPackageUtility.CreateFolderFeedV3Async(
                        pathContext.PackageSource,
                        PackageSaveMode.Defaultv3,
                        packageX);
                }


                var buildFixture = new XPlatMsbuildTestFixture();
                var addResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"add {projectA.ProjectPath} package packageX --version {currentVersion} --no-restore",
                    waitForExit: true);
                Assert.True(addResult.Success);

                var restoreResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"restore {projectA.ProjectName}.csproj",
                    waitForExit: true);
                Assert.True(restoreResult.Success);

                var listResult = CommandRunner.Run(buildFixture._dotnetCli,
                    Directory.GetParent(projectA.ProjectPath).FullName,
                    $"list {projectA.ProjectPath} package --outdated {args}",
                    waitForExit: true);
                
                Assert.True(ContainsIgnoringSpaces(listResult.AllOutput, $"packageX{currentVersion}{currentVersion}{expectedVersion}"));

            }
        }

        private static bool ContainsIgnoringSpaces(string output, string pattern)
        {
            var commandResultNoSpaces = output.Replace(" ", "");

            return commandResultNoSpaces.ToLowerInvariant().Contains(pattern.ToLowerInvariant());
        }
    }
}