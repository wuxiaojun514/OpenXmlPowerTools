﻿namespace Codeuctivity.OpenXmlPowerTools
{
    public class ListItemTextGetter_zh_CN
    {
        public static string? GetListItemText(int levelNumber, string numFmt)
        {
            var ccTDigitCharacters = new[] {
                "",
                "一",
                "二",
                "三",
                "四",
                "五",
                "六",
                "七",
                "八",
                "九",
            };
            var tenCharacter = "十";
            var hundredCharacter = "百";
            var thousandCharacter = "千";
            var andCharacter = "〇";

            var ccDigitCharacters = new[] {
                "○",
                "一",
                "二",
                "三",
                "四",
                "五",
                "六",
                "七",
                "八",
                "九",
            };

            var thousandsRemainder = levelNumber % 1000;
            var hundredsRemainder = levelNumber % 100;
            var thousands = levelNumber / 1000;
            var hundreds = levelNumber % 1000 / 100;
            var tens = levelNumber % 100 / 10;
            var ones = levelNumber % 10;

            if (numFmt == "chineseCounting")
            {
                if (levelNumber >= 1 && levelNumber <= 9)
                {
                    return ccDigitCharacters[levelNumber];
                }

                if (levelNumber >= 10 && levelNumber <= 19)
                {
                    if (levelNumber == 10)
                    {
                        return tenCharacter;
                    }

                    return tenCharacter + ccDigitCharacters[ones];
                }
                if (levelNumber >= 11 && levelNumber <= 99)
                {
                    if (ones == 0)
                    {
                        return ccDigitCharacters[tens] + tenCharacter;
                    }

                    return ccDigitCharacters[tens] + tenCharacter + ccDigitCharacters[ones];
                }
                if (levelNumber >= 100 && levelNumber <= 999)
                {
                    return ccDigitCharacters[hundreds] + ccDigitCharacters[tens] + ccDigitCharacters[ones];
                }

                if (levelNumber >= 1000 && levelNumber <= 9999)
                {
                    return ccDigitCharacters[thousands] + ccDigitCharacters[hundreds] + ccDigitCharacters[tens] + ccDigitCharacters[ones];
                }

                return levelNumber.ToString();
            }
            if (numFmt == "chineseCountingThousand")
            {
                if (levelNumber >= 1 && levelNumber <= 9)
                {
                    return ccTDigitCharacters[levelNumber];
                }

                if (levelNumber >= 10 && levelNumber <= 19)
                {
                    return tenCharacter + ccTDigitCharacters[ones];
                }

                if (levelNumber >= 20 && levelNumber <= 99)
                {
                    return ccTDigitCharacters[tens] + tenCharacter + ccTDigitCharacters[ones];
                }

                if (levelNumber >= 100 && levelNumber <= 999)
                {
                    if (hundredsRemainder == 0)
                    {
                        return ccTDigitCharacters[hundreds] + hundredCharacter;
                    }

                    if (hundredsRemainder >= 1 && hundredsRemainder <= 9)
                    {
                        return ccTDigitCharacters[hundreds] + hundredCharacter + andCharacter + ccTDigitCharacters[levelNumber % 10];
                    }

                    if (ones == 0)
                    {
                        return ccTDigitCharacters[hundreds] + hundredCharacter + ccTDigitCharacters[tens] + tenCharacter;
                    }

                    return ccTDigitCharacters[hundreds] + hundredCharacter + ccTDigitCharacters[tens] + tenCharacter + ccTDigitCharacters[ones];
                }
                if (levelNumber >= 1000 && levelNumber <= 9999)
                {
                    if (thousandsRemainder == 0)
                    {
                        return ccTDigitCharacters[thousands] + thousandCharacter;
                    }

                    if (thousandsRemainder >= 1 && thousandsRemainder <= 9)
                    {
                        return ccTDigitCharacters[thousands] + thousandCharacter + andCharacter + GetListItemText(thousandsRemainder, numFmt);
                    }

                    if (thousandsRemainder >= 10 && thousandsRemainder <= 99)
                    {
                        return ccTDigitCharacters[thousands] + thousandCharacter + andCharacter + ccTDigitCharacters[tens] + tenCharacter + ccTDigitCharacters[ones];
                    }

                    if (hundredsRemainder == 0)
                    {
                        return ccTDigitCharacters[thousands] + thousandCharacter + ccTDigitCharacters[hundreds] + hundredCharacter;
                    }

                    if (hundredsRemainder >= 1 && hundredsRemainder <= 9)
                    {
                        return ccTDigitCharacters[thousands] + thousandCharacter + ccTDigitCharacters[hundreds] + hundredCharacter + andCharacter + ccTDigitCharacters[ones];
                    }

                    return ccTDigitCharacters[thousands] + thousandCharacter + ccTDigitCharacters[hundreds] + hundredCharacter + ccTDigitCharacters[tens] + tenCharacter + ccTDigitCharacters[ones];
                }
                return levelNumber.ToString();
            }
            if (numFmt == "ideographTraditional")
            {
                var iDigitCharacters = new[] {
                    " ",
                    "甲",
                    "乙",
                    "丙",
                    "丁",
                    "戊",
                    "己",
                    "庚",
                    "辛",
                    "壬",
                    "癸",
                };
                if (levelNumber >= 1 && levelNumber <= 10)
                {
                    return iDigitCharacters[levelNumber];
                }

                return levelNumber.ToString();
            }
            return null;
        }
    }
}