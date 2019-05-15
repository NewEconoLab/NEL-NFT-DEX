using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using System;
using System.ComponentModel;
using System.Numerics;
using Helper = Neo.SmartContract.Framework.Helper;

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
    public class Contract1 : SmartContract
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

        public class NFT {
            public byte[] scriptHash;
            public BigInteger tokenID;
        }

        public class Order {
            public byte[] orderID; //=TXID
            public byte[] seller;
            public NFT[] nFTs;
            public byte[] nFTsHash;
            public byte[] assetID;
            public BigInteger price;
            public byte[] buyer;
            public bool isSold;
        }

        //动态合约调用委托
        delegate object deleDyncall(string method, object[] arr);

        //事件
        public delegate void deleSellOrderCreated(byte[] orderID, Order order);
        [DisplayName("sellOrderCreated")]
        public static event deleSellOrderCreated onSellOrderCreated;

        public delegate void deleSellOrderSold(byte[] orderID, Order order);
        [DisplayName("sellOrderSold")]
        public static event deleSellOrderSold onSellOrderSold;

        public delegate void deleAskOrderCreated(byte[] orderID, Order order);
        [DisplayName("askOrderCreated")]
        public static event deleAskOrderCreated onAskOrderCreated;

        public delegate void deleAskOrderCanceld(byte[] orderID);
        [DisplayName("askOrderCanceld")]
        public static event deleAskOrderCanceld onAskOrderCanceld;

        public delegate void deleAskOrderSold(byte[] orderID, Order order);
        [DisplayName("askOrderSold")]
        public static event deleAskOrderSold onAskOrderSold;

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

                //验证是否已经授权给本合约
                //byte[] resultAllowance = (byte[])dyncall("allowance", input);
                if (resultToken.approved.AsBigInteger() != ExecutionEngine.ExecutingScriptHash.AsBigInteger()) return false;
            }

            return true;
        }

        public static bool transferNEP5(byte[] from, byte[] to, byte[] assetHash, BigInteger amount) {
            //多判断总比少判断好
            if (amount <= 0)
                return false;
            if (from.Length != 20 || to.Length != 20)
                return false;

            //构造入参
            object[] transInput = new object[3];
            transInput[0] = from;
            transInput[1] = to;
            transInput[2] = amount;

            //动态调用执行转账
            deleDyncall dyncall = (deleDyncall)assetHash.ToDelegate();
            bool result = (bool)dyncall("transfer", transInput);

            return result;
        }

        public static bool NFTtransferOwnership(NFT[] nFTs, byte[] to) {
            foreach (NFT nft in nFTs)
            {
                //构造入参
                object[] input = new object[2];
                input[0] = to;
                input[1] = nft.tokenID;

                deleDyncall dyncall = (deleDyncall)nft.scriptHash.ToDelegate();
                //转移所有权
                bool resultTransferFrom = (bool)dyncall("transferFrom", input);
                if (!resultTransferFrom) return false;//只要有一个失败就失败
            }

            return true;
        }

        //NFT(组)售卖创建
        public static bool sellNFT(byte[] seller, byte[] nFTsB, byte[] assetID, BigInteger price) {
            NFT[] nFTs = Helper.Deserialize(nFTsB) as NFT[];

            if (!Runtime.CheckWitness(seller)) return false;
            if (assetID.Length != 20) return false;
            if (price < 0) return false;

            //检查NFT组是否为调用者所有，是否所有NFT都已授权合约
            if (!checkNFTs(nFTs)) return false;

            byte[] TXID = (ExecutionEngine.ScriptContainer as Transaction).Hash;

            Order order = new Order();
            order.orderID = TXID;
            order.seller = seller;
            order.nFTs = nFTs;
            order.nFTsHash = SmartContract.Sha256(Helper.Serialize(nFTs));
            order.assetID = assetID;
            order.price = price;
            order.buyer = new byte[0];
            order.isSold = false;

            StorageMap sellOrderMap = Storage.CurrentContext.CreateMap("sellOrder");
            sellOrderMap.Put(order.orderID, Helper.Serialize(order));
            onSellOrderCreated(order.orderID, order);

            return true;
        }

        //NFT出售订单购买
        public static bool buySellOrder(byte[] orderID,byte[] buyer){
            StorageMap sellOrderMap = Storage.CurrentContext.CreateMap("sellOrder");
            var data = sellOrderMap.Get(orderID);
            if(data.Length > 0) {
                Order order = Helper.Deserialize(data) as Order;

                

                //执行NFT所有权转移
                if (!NFTtransferOwnership(order.nFTs, buyer)) {
                    //此处需要强制抛出异常触发回滚
                    throw new Exception("NFTtransferOwnership Fail！");
                    //return false;
                }


                //执行转账
                if (!transferNEP5(buyer, order.seller, order.assetID, order.price)) {
                    //此处需要强制抛出异常触发回滚
                    throw new Exception("transferNEP5 Fail！");
                    //return false;
                } 

                order.buyer = buyer;
                order.isSold = true;
                sellOrderMap.Put(orderID, Helper.Serialize(order));
                onSellOrderSold(orderID, order);
            }
            else return false;

            return true;
        }

        //NFT（组）求购
        public static bool askNFT(byte[] buyer,byte[] nFTsB, byte[] assetID, BigInteger price)
        {
            NFT[] nFTs = Helper.Deserialize(nFTsB) as NFT[];

            if (!Runtime.CheckWitness(buyer)) return false;
            if (assetID.Length != 20) return false;
            if (price < 0) return false;

            ////检查NFT组是否为调用者所有，是否所有NFT都已授权合约
            //if (!checkNFTs(nFTs)) return false;

            //执行转账到合约暂存
            if (!transferNEP5(buyer, ExecutionEngine.ExecutingScriptHash, assetID, price))
            {
                //此处需要强制抛出异常触发回滚
                throw new Exception("transferNEP5 Fail！");
                //return false;
            }

            byte[] TXID = (ExecutionEngine.ScriptContainer as Transaction).Hash;

            Order order = new Order();
            order.orderID = TXID;
            order.seller = new byte[0];
            order.nFTs = nFTs;
            order.nFTsHash = SmartContract.Sha256(Helper.Serialize(nFTs));
            order.assetID = assetID;
            order.price = price;
            order.buyer = buyer;
            order.isSold = false;

            StorageMap askOrderMap = Storage.CurrentContext.CreateMap("askOrder");
            askOrderMap.Put(order.orderID, Helper.Serialize(order));
            onAskOrderCreated(order.orderID, order);

            return true;
        }

        public static bool cancelAskOrder(byte[] orderID)
        {
            StorageMap askOrderMap = Storage.CurrentContext.CreateMap("askOrder");
            var data = askOrderMap.Get(orderID);
            if (data.Length > 0)
            {
                Order order = Helper.Deserialize(data) as Order;

                if(!Runtime.CheckWitness(order.buyer)) return false;

                if (order.isSold) return false;

                //执行转账退回合约暂存
                if (!transferNEP5(ExecutionEngine.ExecutingScriptHash, order.buyer, order.assetID, order.price))
                {
                    //此处需要强制抛出异常触发回滚
                    throw new Exception("transferNEP5 Fail！");
                    //return false;
                }

                askOrderMap.Delete(orderID);
                onAskOrderCanceld(orderID);
            }
            else return false;

            return true;
        }

        public static bool sellAskOrder(byte[] orderID, byte[] seller) {
            StorageMap askOrderMap = Storage.CurrentContext.CreateMap("askOrder");
            var data = askOrderMap.Get(orderID);
            if (data.Length > 0)
            {
                Order order = Helper.Deserialize(data) as Order;


                //执行NFT所有权转移
                if (!NFTtransferOwnership(order.nFTs, order.buyer))
                {
                    //此处需要强制抛出异常触发回滚
                    throw new Exception("NFTtransferOwnership Fail！");
                    //return false;
                }

                //执行转账取出合约暂存
                if (!transferNEP5(ExecutionEngine.ExecutingScriptHash, seller , order.assetID, order.price))
                {
                    //此处需要强制抛出异常触发回滚
                    throw new Exception("transferNEP5 Fail！");
                    //return false;
                }

                order.seller = seller;
                order.isSold = true;
                askOrderMap.Put(orderID, Helper.Serialize(order));
                onAskOrderSold(orderID, order);
            }
            else return false;

            return true;
        }

        //NFT以物易物
        public static bool barterNFT(NFT[] nFTsSell, NFT[] nFTsBuy)
        {

            return false;
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
                if (operation == "sellNFT")
                {
                    return sellNFT((byte[]) args[0],(byte[]) args[1],(byte[]) args[2],(BigInteger) args[3]);
                }
                if (operation == "buySellOrder")
                {
                    return buySellOrder((byte[])args[0], (byte[])args[1]);
                }
            }

            return false;
        }
    }
}
