using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ViDi2.VisionPro;
using ViDi2;
using VidiReadResults;

namespace Vistalink.ViDiSegmenter // Version2
{
    /* 
    Class for segmenting results from ViDi Read tool for DOT code reading on tire, with the objective of seperating DOT and DATE
    and removing rubbish characters at the end of the string.
    A = longest string: DOT+DATE 
    B = DOT only  
    (Sorting A <--> B has to be done before)

    Consider space when difference between two positions is > dev * characterwidth
    The spaces between the segments are evaluated and used as basis for segmenting.
    */
    public class Segmenter
    {
        public ViDiReadResultList GenerateList(SegmentInput Input)
            /* Generate lists of all relevant data: Score, X-position, width */
        {
            ViDiReadResultList result = new ViDiReadResultList();
            string readResult="";
            List<double> xpos_list = new List<double>(); //list for x-positions of start of new segment
            List<double> w_list = new List<double>(); //list for width of characters
            List<double> score_list = new List<double>(); //list for score of characters

            if (Input.Marking != null && Input.OcrString != null)
            {
                readResult = Input.OcrString;
                IBlueMarking m = Input.Marking;
                Point p = new Point();
                
                int n = readResult.Length;
                int i = 0;
                while (i<n)
                {
                    try
                    {
                        p = m.Views[0].Matches[0].Features[i].Position; //We assume only 1 model match and this model is 0
                        p = p.ToOriginalFromView(m.Views[0]); //Tranformation back to pixel image from Vidi image
                        xpos_list.Add(p.X);
                        score_list.Add(m.Views[0].Matches[0].Features[i].Score);
                        w_list.Add(m.Views[0].Matches[0].Features[i].Size.Width);
                    }
                    catch { }
                }
            }
            
            else
            {
                readResult = "";         
            }

            result.Read = readResult;
            result.ScoreList = score_list;
            result.XPosList = xpos_list;
            result.WidthList = w_list;

            return result;
        }
       
        //Before segmenting we want to get rid of rubbish in the back of the string
        public PreFilterResult PreFilter(SegmentInput Input)
        {

            PreFilterResult result = new PreFilterResult();
            string str = Input.OcrString;
            if ( str != null)
            { 
                int length = Input.OcrString.Length;
                if (length>0)
                {
                        byte[] ascii = Encoding.ASCII.GetBytes(str);
                        char[] str_char = str.ToCharArray(); //string is read-only, have to modify the character array and convert back to string
                        var char_list = new List<char>(str_char); //Convert to List to be able to easily remove at position
                        //removing characters which not numbers or letters, start from right to left
                        //only remove at the end of the string
                        int i = length-1;
                        while (i > 7)  //Min. Dot length = 7
                        {
                            int a = (int)ascii[i];
                            if (!((a>47 && a <58 ) || (a>64 && a< 91)) ) //Only capital letters and numbers 0-9 allowed
                            {
                                char_list.RemoveAt(i);
                            }
                            else
                            {
                                break; //only remove rubbisch char at end of string (, we keep order of indexes in List)
                            }
                            i--;
                        }
                    str_char = char_list.ToArray();
                    str = new string(str_char);
                }
            }
            else
            {
                str = "";
            }


            result.OcrFiltered = str;
            return result;
            
        }

        public SegmentResult Segment(SegmentInput input, double devW)
        {
            //Segment into list of Segment indexes and spaces
            SegmentResult result = new SegmentResult();
            PreFilterResult filtered = PreFilter(input);//Filter rubbish end of string
            input.OcrString = filtered.OcrFiltered;
            ViDiReadResultList lists = GenerateList(input); //Use filtered string to generate lists

            double w = 0;
            double xCurr = 0;
            double xPrev = 0;
            int nChar = lists.Read.Length;
            double dev = devW; //consider space when difference between two positions is > dev * characterwidth

            List<double> x_list = new List<double>(); //list for width of characters
            x_list = lists.XPosList;
            List<double> w_list = new List<double>(); //list for width of characters
            w_list = lists.WidthList;
            List<double> segm_x_list = new List<double>(); //list for x-positions of start of new segment
            List<int> segm_i_list = new List<int>(); //list for character indexes of start of new segment
            List<double> space_list = new List<double>(); //list for size of spaces of segment

            if (nChar > 0 && x_list.Count == w_list.Count && x_list.Count == nChar) //Lists have same number of elements(should be the case)
            {
                w = w_list.Max(); //retain widest character as reference for calculating space
                                  //First segment starts at first character - index 0 for X-position
                xCurr = x_list[0];
                segm_x_list.Add(x_list[0]);
                segm_i_list.Add(0);
                //Loop through the remaining characters --> list of index positions with start of segments
                xPrev = xCurr;

                int i = 1; //start at 1, we handled 0 above
                while (i < nChar)
                {
                    xCurr = x_list[i];

                    if ((xCurr - xPrev) > (dev * w)) //consider space when difference between two positions is > dev * characterwidth -->New segment
                    {
                        segm_x_list.Add(xCurr);
                        segm_i_list.Add(i);
                        space_list.Add(xCurr - xPrev);
                    }

                    xPrev = xCurr;

                    i++;
                }
            }

            else
            {
                //
            }
            result.Nsegments = segm_i_list.Count; //Get number of segments
            result.SegmentIndexList = segm_i_list;
            result.SpaceList = space_list;
            result.FilteredString = lists.Read;

            return result;

        }

        public ResultA SegmentA(SegmentInput A, double devW)
        {
            //Segment A in Dot + Date
            ResultA result = new ResultA(); //final result for A

            SegmentResult segmentRes = Segment(A, devW); //Segment A --> list of indexes and spaces

            int n = segmentRes.FilteredString.Length;
            int indexStartDate = 0;
            int indexEndDate = 0;
            string str = segmentRes.FilteredString;
            string date = "";
            string dot = "";

            List<int> indexList = new List<int>();
            indexList = segmentRes.SegmentIndexList;

            // METHOD ADRIAAN
            //We have segmented first, now we want to check if the last segment is indeed the date timestamp
            //We work from right to left!
            if (n>0 && indexList.Count>1) //At least 2 segments
            {
                int i = (indexList.Count - 1); //Start from the last segment (ex. count = 12 --> last index = 11)
                int j = n; //end of last segment
                while (i > 0 )//At least 2 segments
                {

                    if ((j - indexList[i]) > 3)  //timestamp = 4 karakters, length of segment
                    {
                        indexStartDate = indexList[i];

                        if (i != (indexList.Count - 1))//if not the last segment
                        {
                            indexEndDate = indexList[i + 1] - 1; //previous index of segment -1, is end of current segment
                        }
                        else //if last segment
                        {
                            indexEndDate = n - 1;
                        }
                        break;
                    }

                    j = indexList[i]; // length of next segment = previous index - start index next segment
                    i--; //Work from right to left

                }
            }
            if (n > 3 && indexList.Count == 1) //Only 1 segment and string at least 4 char --> we assume last 4 characters are timestamp
            {
                indexEndDate = n - 1;
                indexStartDate = n - 4;
            }

            if (indexStartDate >0 && indexEndDate >= (indexStartDate+4))
            {

                date = str.Substring(indexStartDate, ((indexEndDate + 1) - indexStartDate));
                dot = str.Substring(0, indexStartDate);
            }
            else
            {
                date = "0000";
                indexEndDate = n;
                dot = str;
            }


                result.Date = date;
                result.Dot = dot;
                result.IndexDateEnd = indexEndDate;
                result.IndexDateStart = indexStartDate;
               
            
           
                   return result;
        }


            // METHOD ALAIN:  KORTER, EVENTUEEL UITZOEKEN... WERKT NOG NIET ivm indexes
            /*
            // Splitted string at index position

            string temp = str;
            for (int i = 0; i < indexList.Count; i++)
            {
                temp = temp.Insert(i + indexList[i], " ");
                Debug.WriteLine(temp);
            }
            string[] splitted = temp.Trim(new char[] { ' ' }).Split(' ');

            // Found date . First 4 lenght string on the end and add '*' before and at the end
            int p = splitted.Length - 1;
            bool DateFound = false;
            while (p >= 0 && !DateFound)
            {
                Debug.WriteLine(splitted[p]);
                if (splitted[p].Length == 4)
                {
                    result.IndexDateStart = indexList[p - 1];
                    result.IndexDateEnd = indexList[p] - 1;
                    splitted[p] = $"*{splitted[p]}*";
                }
                p--;
            }

            // Concate all to one string 
            string temp2 = String.Concat(splitted);

            // DOT*DATE*CLUTTER => DOT,DATE,CLUTTER
            string[] Splitted2 = temp2.Split('*');
            result.Dot = Splitted2[0];
            result.Date = Splitted2[1];
            string Clutter = Splitted2[2];

            return result;
        }
        */


            public ResultB SegmentB(SegmentInput B, double devW, double devSpace)
        {
            //Remove clutter from end of string of B, we presume only one segment of clutter and with a bigger space
            ResultB result = new ResultB(); //final result for B

            SegmentResult segmentRes = Segment(B, devW); //Segment B --> list of indexes and spaces

            int nSpace = segmentRes.SpaceList.Count;
            int nSegm = segmentRes.SegmentIndexList.Count;
            string str = segmentRes.FilteredString;
            int nStr = segmentRes.FilteredString.Length;
            int indexDotEnd = 0;

            if (nStr >0) //At least 1 char in string
            {
                indexDotEnd = nStr -1; //We assume last segment is not clutter or have no previous space to compare to
            
                if (nSpace > 1) //At least 2 spaces present--> compare last space to 1st space
                {
                    double spaceRef = segmentRes.SpaceList[0]; //take first space as reference

                    if (segmentRes.SpaceList[nSpace - 1] > devSpace * spaceRef) //last space larger than allowed deviation (= devSpace * spaceRef)
                    {
                        //last segment is clutter
                        indexDotEnd = segmentRes.SegmentIndexList[nSegm - 1] - 1;//index of last segment - 1 = end of previous segm
                    }
                
                }
                result.Dot = str.Substring(0, indexDotEnd + 1);
            }
            else //if nStr=0
            {
                indexDotEnd = 0;
                result.Dot = "";
            }


            result.IndexDotStart = 0;//We presume dot always starts at 0 (no clutter in beginning)
            result.IndexDotEnd = indexDotEnd;
            

            return result;
        }

        public FinalResult SegmentAB(SegmentInput A, SegmentInput B, double devW, double devSpace)
        {
            FinalResult result = new FinalResult();

            ResultA resA = SegmentA(A, devW);
            ResultB resB = SegmentB(B, devW, devSpace);

            result.Date = resA.Date;
            result.DotA = resA.Dot;
            result.IndexDateEnd = resA.IndexDateEnd;
            result.IndexDateStart = resA.IndexDateStart;
            result.DotB = resB.Dot;
            result.IndexDotBStart = resB.IndexDotStart;
            result.IndexDotBEnd = resB.IndexDotEnd;

            return result;
        }

        public DoublesRemoved RemoveDoubles(SegmentInput Input, int StartIndex, int EndIndex, double devP)
        //Code to remove double characters at the same position based on the score
        {
            DoublesRemoved result = new DoublesRemoved();

            IBlueMarking m = Input.Marking;
            int n = (EndIndex+1);
            string ocr = Input.OcrString;
            Point p = new Point();

            char[] ocr_char = ocr.ToCharArray(); //string[int] is read-only--> create and modify the character array and convert back to string
            List<char> char_list = new List<char>(ocr_char); //Convert to List to be able to easily remove at certain position
            List<char> newChar_list = new List<char>(); //list to build new string (no doubles)
             
            List<double> score_list = new List<double>(); //list for score (to build result array later)
            List<double> pos_list = new List<double>(); //list for positions (to build result array later)

            double w = 0; //Char width
            double dev = devP;//consider same when difference between two positions is < dev * characterwidth
            double x_curr = 0;
            double x_prev = 0;
            double score_prev = 0;
            double score_curr = 0;

            try
            {
                if (m!=null && n >1) //Marking present and at least 2 characters
                {
                    int i = StartIndex; //index in Marking
                    int j = 0; //index for newChar_list
                    while (i < n) //loop trough all of characters
                    {
                        w = m.Views[0].Matches[0].Features[i].Size.Width; //Get char width
                        p = m.Views[0].Matches[0].Features[i].Position;
                        p = p.ToOriginalFromView(m.Views[0]);
                        x_curr = p.X;
                        score_curr = m.Views[0].Matches[0].Features[i].Score;

                        if (((x_curr - x_prev) < (dev * w)) && (j > 0)) //Compare scores when two char at one position (but not for 1st index of loop)
                        {
                            if (score_curr >= score_prev)
                            {
                                newChar_list.RemoveAt(j - 1);
                                score_list.RemoveAt(j - 1);
                                pos_list.RemoveAt(j - 1);

                                newChar_list.Add(char_list[j]);
                                score_list.Add(score_curr);
                                pos_list.Add(x_curr);
                            }
                            else
                            {
                                //Niks doen (niet toevoegen aan list)
                            }

                        }
                        else
                        {
                            newChar_list.Add(char_list[j]);
                            score_list.Add(score_curr);
                            pos_list.Add(x_curr);
                        }
                        x_prev = x_curr;
                        score_prev = score_curr;
                        i++;
                        j++;
                    }
                    ocr_char = newChar_list.ToArray();
                    result.DbleRemovedStr = ocr = new string(ocr_char);
                    
                }
                else
                {
                    result.DbleRemovedStr = "";
                }

                result.CharList = newChar_list;
                result.PosList = pos_list;
                result.ScoreList = score_list;
            }
            catch  { result.DbleRemovedStr = "";}

            return result;
        }
        public VidiReadResults.VidiReadResults[] GenerateVidiReadResults(SegmentInput Input, int StartIndex, int EndIndex, double devP)
            //Generate array of ViDiReadResults
        {

            DoublesRemoved dblsRemoved = RemoveDoubles(Input, StartIndex, EndIndex, devP);
            int n = dblsRemoved.DbleRemovedStr.Length;
            VidiReadResults.VidiReadResults[] results = new VidiReadResults.VidiReadResults[n];
            if (n>0)
            {
                try
                {
                    int j = 0;
                    while (j < n)
                    {
                        VidiReadResults.VidiReadResults res = new VidiReadResults.VidiReadResults('0', 0, 0)
                        {
                            Character = dblsRemoved.CharList[j],
                            Score = dblsRemoved.ScoreList[j],
                            Position = dblsRemoved.PosList[j]
                        };
                        results[j] = res;
                        j++;
                    }
                }
                catch { }
            }
            
            return results;
        }
        public string FixTimestamp(string Input)
        {
            string ts = Input;

            try
            {
                int length = ts.Length;
                byte[] ascii = Encoding.ASCII.GetBytes(ts);
                char[] ts_char = ts.ToCharArray(); //string is read-only, have to modify the character array and convert back to string

                if (length > 4)
                {
                    var char_list = new List<char>(ts_char); //Convert to List to be able to easily remove at position

                    int i = 0;
                    while (i < length && char_list.Count > 4)
                    {
                        

                        int a = (int)ascii[i];

                        if (!(a >= 30 && a <= 39))
                        {
                            char_list.RemoveAt(i);   //Now we are not checking position, only removing excess characters, to be 100% we should check position from the ViDi Marking
                        }
                        i++;
                    }

                    ts_char = char_list.ToArray();
                    ts = new string(ts_char);
                }
                //Now ts is length 4, check remaining characters
                length = ts.Length;
                ascii = Encoding.ASCII.GetBytes(ts);
                ts_char = ts.ToCharArray();

                if (length == 4)
                {
                    int i = 0;
                    while (i < 4)
                    {
                        int a = (int)ascii[i];
                        if (!(a >= 30 && a <= 39))
                        {
                            switch (a)
                            {
                                case 66: //B to 8
                                    ts_char[i] = '8';
                                    break;
                                case 68: //D to 0
                                    ts_char[i] = '0';
                                    break;
                                case 74: //J to 1
                                    ts_char[i] = '1';
                                    break;
                                case 79: //O to 0
                                    ts_char[i] = '0';
                                    break;
                            }
                        }
                        i++;
                    }

                    ts = new string(ts_char);
                }


                if (length < 4) { ts = "0000"; }

            }
            catch { }


            return ts;
        }

    }



   

    [Serializable]
    public class ViDiReadResultList
    {
        public List<double> XPosList { get; set; } = new List<double>(); //List of X-positions of features
        public List<double> ScoreList { get; set; } = new List<double>(); //List of scores
        public List<double> WidthList { get; set; } = new List<double>(); //List of widths of features
        public string Read { get; set; }  //Decoded string
       
    }
    public class SegmentResult
    {
        public int Nsegments { get; set; }
        public List<double> Xpos { get; set; } = new List<double>(); 
        public List<int> SegmentIndexList { get; set; } = new List<int>(); //List of indexes of segments
        public List<double> SpaceList { get; set; } = new List<double>(); 
        public string FilteredString { get; set; }
    }
    public class ResultA
    {
        public string Dot { get; set; }
        public string Date { get; set; }
        public int IndexDateStart { get; set; }
        public int IndexDateEnd { get; set; }
    }
    public class ResultB
    {
        public string Dot { get; set; }
        public int IndexDotStart { get; set; }
        public int IndexDotEnd { get; set; }
    }
    public class FinalResult
    {
        public string DotB { get; set; }
        public int IndexDotBStart { get; set; }
        public int IndexDotBEnd { get; set; }
        public string DotA { get; set; }
        public string Date { get; set; }
        public int IndexDateStart { get; set; }
        public int IndexDateEnd { get; set; }
    }
    public class SegmentInput
    {
        public IBlueMarking Marking;
        public string OcrString;
        public int NumberCharacters;
    }
    public class PreFilterResult
    {
    public string OcrFiltered { get; set; }
    }
    public class DoublesRemoved
    {
        public string DbleRemovedStr{ get; set; }
        public List<char> CharList { get; set; } = new List<char>(); //List of spositions
        public List<double> ScoreList { get; set; } = new List<double>(); //List of scores
        public List<double> PosList { get; set; } = new List<double>(); //List of spositions
    }

}


 

