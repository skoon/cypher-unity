using NUnit.Framework;
using UnityEngine;

namespace Cypher.Tests
{
    /// <summary>The settings file lives under the company and product name, so they are part of the player's data.</summary>
    public class ProjectIdentityTests
    {
        [Test]
        public void CompanyAndProductNamesAreCypher()
        {
            Assert.AreEqual("Cypher", Application.companyName);
            Assert.AreEqual("CYPHER", Application.productName);
        }

        [Test]
        public void PersistentDataLivesUnderCompanyAndProduct()
        {
            string path = Application.persistentDataPath.Replace('\\', '/');
            StringAssert.EndsWith("/Cypher/CYPHER", path);
        }
    }
}
