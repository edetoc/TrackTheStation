using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrackTheStation.Models
{
    public class Astronaut
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string RoleOnISS { get; set; }

        public string Agency { get; set; }

        public Uri Picture { get; set; }

        public Astronaut(string firstname, string lastname, string roleonISS, string agency, Uri picture)
        {
            FirstName = firstname;
            LastName = lastname;
            RoleOnISS = roleonISS;
            Agency = agency;
            Picture = picture;

        }

    }
}
