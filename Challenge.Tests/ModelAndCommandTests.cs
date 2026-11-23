using System;
using Challenge.Models;
using Challenge.ViewModels;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Challenge.Tests
{
    [TestClass]
    public class ModelTests
    {
        [TestMethod]
        [DataRow((string)null)]
        [DataRow("")]
        [DataRow("  ")]
        public void Currency_WithoutCode_Throws(string code)
        {
            Assert.ThrowsExactly<ArgumentException>(() => new Currency(code, "Name"));
        }

        [TestMethod]
        public void Currency_DisplayName_CombinesCodeAndName()
        {
            Assert.AreEqual("EUR - Euro", new Currency("EUR", "Euro").DisplayName);
            Assert.AreEqual("EUR", new Currency("EUR", null).DisplayName);
        }

        [TestMethod]
        public void ExchangeRate_IgnoresTimeOfDay()
        {
            var rate = new ExchangeRate(new DateTime(2024, 1, 10, 15, 30, 0), 1.1m);

            Assert.AreEqual(new DateTime(2024, 1, 10), rate.Date);
        }
    }

    [TestClass]
    public class RelayCommandTests
    {
        [TestMethod]
        public void Execute_RunsTheAction()
        {
            var executed = false;
            var command = new RelayCommand(() => executed = true);

            command.Execute(null);

            Assert.IsTrue(executed);
        }

        [TestMethod]
        public void CanExecute_FollowsThePredicate()
        {
            var enabled = false;
            var command = new RelayCommand(() => { }, () => enabled);

            Assert.IsFalse(command.CanExecute(null));
            enabled = true;
            Assert.IsTrue(command.CanExecute(null));
        }

        [TestMethod]
        public void RaiseCanExecuteChanged_NotifiesSubscribers()
        {
            var command = new RelayCommand(() => { });
            var raised = 0;
            command.CanExecuteChanged += (sender, args) => raised++;

            command.RaiseCanExecuteChanged();

            Assert.AreEqual(1, raised);
        }

        [TestMethod]
        public void Constructor_WithoutAction_Throws()
        {
            Assert.ThrowsExactly<ArgumentNullException>(() => new RelayCommand(null));
        }
    }
}
