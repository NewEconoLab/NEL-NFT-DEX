using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

//0x394b3ccd59d7bfb00f5d101fd6e872fe159ca069 test042902

/*
交易类型：

NFT兑NEP5：
一口价
荷兰拍
带价求购

NFT兑NFT：
NFT交换邀约（不清楚兑换物） N-N
带NFT求购（明确兑换物） N-N

NFTpackage{
    [
        {
            scriptHash,
            tokenID
        },
        {
            scriptHash,
            tokenID
        }
    ]domainCenterHash
}
 
*/

namespace NEL_NFT_DEX
{
    public class test0 : SmartContract
    {
        public class Token
        {
            public Token()
            {
                token_id = 0;
                owner = new byte[0];
                approved = new byte[0];
                properties = "";
                uri = "";
                rwProperties = "";
            }
            //不能使用get set

            public BigInteger token_id;// { get; set; } //代币ID
            public byte[] owner;//  { get; set; } //代币所有权地址
            public byte[] approved;//  { get; set; } //代币授权处置权地址
            public string properties;//  { get; set; } //代币只读属性
            public string uri;//  { get; set; } //代币URI链接
            public string rwProperties;//  { get; set; } //代币可修改属性
        }

        static readonly byte[] sh = Helper.ToScriptHash("AbpkfkKqxzYuoJECDUJqzq3UXaZ2BBZGXJ");

        public delegate void deleLog(BigInteger rowID,string infoName, object info);
        [DisplayName("log")]
        public static event deleLog onLog;

        //动态合约调用委托
        delegate object deleDyncall(string method, object[] arr);

        public class NFT
        {
            public byte[] scriptHash;
            public BigInteger tokenID;
        }

        public static NFT[] getNFTs() {
            StorageMap nFTsMap = Storage.CurrentContext.CreateMap("nFTs");
            byte[] data = nFTsMap.Get("nFTs");
            if (data.Length > 0)
            {
                return Helper.Deserialize(data) as NFT[];
            }
            else {
               
                NFT[] nFTs = new NFT[] {
                    new NFT {
                        scriptHash= sh,
                        tokenID = 5
                        },
                    new NFT {
                        scriptHash= sh,
                        tokenID = 20
                        },
                    new NFT {
                        scriptHash= sh,
                        tokenID = 300
                        }
                };

                return nFTs;
            }
        }

        public static byte[] getNFTsBytes() {
            return Helper.Serialize(getNFTs());
        }

        public static bool setNFTs(byte[] nFTsB) {
            NFT[] nFTs = Helper.Deserialize(nFTsB) as NFT[];
            StorageMap nFTsMap = Storage.CurrentContext.CreateMap("nFTs");
            nFTsMap.Put("nFTs",Helper.Serialize(nFTs));

            return true;
        }

        public static bool clearNFTs() {
            StorageMap nFTsMap = Storage.CurrentContext.CreateMap("nFTs");
            nFTsMap.Delete("nFTs");

            return true;
        }



        public static bool checkNFTs(NFT[] nFTs)
        {
            foreach (NFT nft in nFTs)
            {
                //构造入参
                object[] input = new object[1];
                input[0] = nft.tokenID;

                //动态调用NFT合约
                deleDyncall dyncall = (deleDyncall)nft.scriptHash.ToDelegate();
                Token resultToken = (Token)dyncall("token", input);

                //验证所有权
                if (!Runtime.CheckWitness(resultToken.owner)) return false;

                onLog(149, "approved", resultToken.approved);
                onLog(149, "approvedBI", resultToken.approved.AsBigInteger());
                onLog(149, "ExecutingScriptHash", ExecutionEngine.ExecutingScriptHash);
                onLog(149, "ExecutingScriptHashBI", ExecutionEngine.ExecutingScriptHash.AsBigInteger());

                //验证是否已经授权给本合约
                //byte[] resultAllowance = (byte[])dyncall("allowance", input);
                if (resultToken.approved.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger()) return false;
            }

            return true;
        }

        public static object Main(string operation, object[] args)
        {
            //UTXO转账转入转出都不允许
            if (Runtime.Trigger == TriggerType.Verification || Runtime.Trigger == TriggerType.VerificationR)
            {
                return false;
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                if (operation == "setNFTs")
                {
                    return setNFTs((byte[])args[0]);
                }
                if (operation == "getNFTs")
                {
                    return getNFTs();
                }
                if (operation == "getNFTsBytes")
                {
                    return getNFTsBytes();
                }
                if (operation == "clearNFTs")
                {
                    return clearNFTs();
                }
                if (operation == "checkNFTsByBytes")
                {
                    return checkNFTs(Helper.Deserialize((byte[])args[0]) as NFT[]);
                }
                if (operation == "checkNFTsByStorage")
                {
                    return checkNFTs(getNFTs());
                }
            }

            return false;
        }
    }
}
