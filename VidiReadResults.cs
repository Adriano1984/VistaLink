using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VidiReadResults
{
   [Serializable]
    public class VidiReadResults

    {

        public char Character { get; set; }

        public double Score { get; set; }

        public double Position { get; set; }

        public VidiReadResults(char character, double score, double position)

        {

            Character = character;

            Score = score;

            Position = position;

        }

        public string GetDetails()

        {
            string details = Character.ToString() + " " + Score.ToString() + " " + Position.ToString();
            return details;


        }
    }
}