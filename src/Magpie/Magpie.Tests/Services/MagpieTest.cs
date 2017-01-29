﻿using System;
using System.Linq;
using Magpie.Tests.Mocks;
using MagpieUpdater.Models;
using MagpieUpdater.Services;
using MagpieUpdater.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace Magpie.Tests.Services
{
    [TestClass]
    public class MagpieTest
    {
        private MockMagpie _mockMagpie;

        [TestInitialize]
        public void Initialize()
        {
            AssemblyInjector.Inject();
            _mockMagpie = new MockMagpie("validContentUrl");
        }

        [TestMethod]
        public void TestValidJson()
        {
            _mockMagpie.CheckInBackground();
            var appcast = _mockMagpie.RemoteAppcast;
            Assert.IsNotNull(appcast);
            Assert.AreEqual("bar", appcast.RawDictionary["foo"]);
            Assert.AreEqual(new Version(5, 8, 8), appcast.Channels.First().Version);
            Assert.AreEqual(2, appcast.RawDictionary.Count);
        }

        [TestMethod]
        public void TestAppcastAvailableRaiseEvent()
        {
            var raised = false;
            _mockMagpie.RemoteAppcastAvailableEvent += (s, a) => { raised = true; };
            _mockMagpie.CheckInBackground();
            Assert.IsTrue(raised);
        }

        [TestMethod]
        public void TestNoUpdatesWindowShownOnForceCheck()
        {
            var updateDecider = Substitute.For<UpdateDecider>(new DebuggingWindowViewModel());
            updateDecider.ShouldUpdate(Arg.Any<Channel>(), true).Returns(false);
            _mockMagpie.UpdateDecider = updateDecider;
            _mockMagpie.ForceCheckInBackground();
            Assert.IsTrue(_mockMagpie._showNoUpdatesWindowFlag);
        }

        [TestMethod]
        public void TestNoUpdatesWindowNotShownOnNormalCheck()
        {
            var updateDecider = Substitute.For<UpdateDecider>(new DebuggingWindowViewModel());
            updateDecider.ShouldUpdate(Arg.Any<Channel>()).Returns(false);
            _mockMagpie.UpdateDecider = updateDecider;
            _mockMagpie.CheckInBackground();
            Assert.IsFalse(_mockMagpie._showNoUpdatesWindowFlag);
        }

        [TestMethod]
        public void TestUpdateWindowShown()
        {
            _mockMagpie.UpdateDecider = Substitute.For<UpdateDecider>(new DebuggingWindowViewModel());
            _mockMagpie.UpdateDecider.ShouldUpdate(Arg.Any<Channel>(), true).Returns(true);
            _mockMagpie.ForceCheckInBackground();
            Assert.IsTrue(_mockMagpie._showUpdateWindowFlag);
        }

        [TestMethod]
        public void TestForceCheckOverridingAppCastUrl()
        {
            _mockMagpie.UpdateDecider = Substitute.For<UpdateDecider>(new DebuggingWindowViewModel());
            _mockMagpie.UpdateDecider.ShouldUpdate(Arg.Any<Channel>(), true).Returns(true);
            _mockMagpie.ForceCheckInBackground("alternateUrl");
            _mockMagpie._remoteContentDownloader.Received(1).DownloadStringContent("alternateUrl");
        }

        [TestMethod]
        public void TestBackgroundCheckOverridingAppCastUrl()
        {
            _mockMagpie.UpdateDecider = Substitute.For<UpdateDecider>(new DebuggingWindowViewModel());
            _mockMagpie.UpdateDecider.ShouldUpdate(Arg.Any<Channel>(), true).Returns(true);
            _mockMagpie.CheckInBackground("alternateUrl");
            _mockMagpie._remoteContentDownloader.Received(1).DownloadStringContent("alternateUrl");
        }
    }
}