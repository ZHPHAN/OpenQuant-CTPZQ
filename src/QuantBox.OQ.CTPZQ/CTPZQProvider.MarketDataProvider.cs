﻿using System;
using System.ComponentModel;
using QuantBox.CSharp2CTPZQ;
using SmartQuant.Data;
using SmartQuant.FIX;
using SmartQuant.Instruments;
using SmartQuant.Providers;

namespace QuantBox.OQ.CTPZQ
{
    public partial class CTPZQProvider : IMarketDataProvider
    {
        private IBarFactory factory;

        public event MarketDataRequestRejectEventHandler MarketDataRequestReject;
        public event MarketDataSnapshotEventHandler MarketDataSnapshot;
        public event BarEventHandler NewBar;
        public event BarEventHandler NewBarOpen;
        public event BarSliceEventHandler NewBarSlice;
        public event CorporateActionEventHandler NewCorporateAction;
        public event FundamentalEventHandler NewFundamental;
        public event BarEventHandler NewMarketBar;
        public event MarketDataEventHandler NewMarketData;
        public event MarketDepthEventHandler NewMarketDepth;
        public event QuoteEventHandler NewQuote;
        public event TradeEventHandler NewTrade;

        #region IMarketDataProvider
        [Category(CATEGORY_BARFACTORY)]
        public IBarFactory BarFactory
        {
            get
            {
                return factory;
            }
            set
            {
                if (factory != null)
                {
                    factory.NewBar -= new BarEventHandler(OnNewBar);
                    factory.NewBarOpen -= new BarEventHandler(OnNewBarOpen);
                    factory.NewBarSlice -= new BarSliceEventHandler(OnNewBarSlice);
                }
                factory = value;
                if (factory != null)
                {
                    factory.NewBar += new BarEventHandler(OnNewBar);
                    factory.NewBarOpen += new BarEventHandler(OnNewBarOpen);
                    factory.NewBarSlice += new BarSliceEventHandler(OnNewBarSlice);
                }
            }
        }

        private void OnNewBar(object sender, BarEventArgs args)
        {
            if (NewBar != null)
            {
                Bar bar = args.Bar;
                if (null != MarketDataFilter)
                {
                    Bar b = MarketDataFilter.FilterBar(bar, args.Instrument.Symbol);
                    if (null != b)
                    {
                        NewBar(this, new BarEventArgs(b, args.Instrument, this));
                    }
                }
                else
                {
                    NewBar(this, new BarEventArgs(bar, args.Instrument, this));
                }
            }
        }

        private void OnNewBarOpen(object sender, BarEventArgs args)
        {
            if (NewBarOpen != null)
            {
                Bar bar = args.Bar;
                if (null != MarketDataFilter)
                {
                    Bar b = MarketDataFilter.FilterBarOpen(bar, args.Instrument.Symbol);
                    if (null != b)
                    {
                        NewBarOpen(this, new BarEventArgs(b, args.Instrument, this));
                    }
                }
                else
                {
                    NewBarOpen(this, new BarEventArgs(bar, args.Instrument, this));
                }
            }
        }

        private void OnNewBarSlice(object sender, BarSliceEventArgs args)
        {
            if (NewBarSlice != null)
            {
                NewBarSlice(this, new BarSliceEventArgs(args.BarSize, this));
            }
        }

        public void SendMarketDataRequest(FIXMarketDataRequest request)
        {
            if (!_bMdConnected)
            {
                EmitError(-1, -1, "行情服务器没有连接");
                mdlog.Error("行情服务器没有连接");
                return;
            }

            bool bSubscribe = false;
            bool bTrade = false;
            bool bQuote = false;
            bool bMarketDepth = false;
            if (request.NoMDEntryTypes > 0)
            {
                switch (request.GetMDEntryTypesGroup(0).MDEntryType)
                {
                    case '0':
                    case '1':
                        if (request.MarketDepth != 1)
                        {
                            bMarketDepth = true;
                            break;
                        }
                        bQuote = true;
                        break;
                    case '2':
                        bTrade = true;
                        break;
                }
            }
            bSubscribe = (request.SubscriptionRequestType == DataManager.MARKET_DATA_SUBSCRIBE);

            if (bSubscribe)
            {
                for (int i = 0; i < request.NoRelatedSym; ++i)
                {
                    FIXRelatedSymGroup group = request.GetRelatedSymGroup(i);
                    Instrument inst = InstrumentManager.Instruments[group.Symbol];

                    //将用户合约转成交易所合约
                    string altSymbol = inst.GetSymbol(this.Name);
                    string altExchange = inst.GetSecurityExchange(this.Name);
                    string _altSymbol = GetApiSymbol(altSymbol);
                    CZQThostFtdcInstrumentField _Instrument;
                    if (_dictInstruments.TryGetValue(altSymbol, out _Instrument))
                    {
                        _altSymbol = _Instrument.InstrumentID;
                        altExchange = _Instrument.ExchangeID;
                    }

                    DataRecord record;
                    if (!_dictAltSymbol2Instrument.TryGetValue(altSymbol, out record))
                    {
                        record = new DataRecord();
                        record.Instrument = inst;
                        record.Symbol = _altSymbol;
                        record.Exchange = altExchange;
                        _dictAltSymbol2Instrument[altSymbol] = record;

                        mdlog.Info("订阅合约 {0} {1} {2}", altSymbol, record.Symbol, record.Exchange);
                        MdApi.MD_Subscribe(m_pMdApi, record.Symbol, record.Exchange);

                        if (_bTdConnected)
                        {
                            TraderApi.TD_ReqQryInvestorPosition(m_pTdApi, null);
                            timerPonstion.Enabled = false;
                            timerPonstion.Enabled = true;
                        }
                    }

                    //记录行情,同时对用户合约与交易所合约进行映射
                    CZQThostFtdcDepthMarketDataField DepthMarket;
                    if (!_dictDepthMarketData.TryGetValue(altSymbol, out DepthMarket))
                    {
                        _dictDepthMarketData.Add(altSymbol, DepthMarket);
                    }

                    if (bTrade)
                        record.TradeRequested = true;
                    if (bQuote)
                        record.QuoteRequested = true;
                    if (bMarketDepth)
                        record.MarketDepthRequested = true;

                    if (bMarketDepth)
                    {
                        inst.OrderBook.Clear();
                    }
                }
            }
            else
            {
                for (int i = 0; i < request.NoRelatedSym; ++i)
                {
                    FIXRelatedSymGroup group = request.GetRelatedSymGroup(i);
                    Instrument inst = InstrumentManager.Instruments[group.Symbol];

                    //将用户合约转成交易所合约
                    string altSymbol = inst.GetSymbol(this.Name);

                    DataRecord record;
                    if (!_dictAltSymbol2Instrument.TryGetValue(altSymbol, out record))
                    {
                        break;
                    }

                    if (bTrade)
                        record.TradeRequested = false;
                    if (bQuote)
                        record.QuoteRequested = false;
                    if (bMarketDepth)
                        record.MarketDepthRequested = false;

                    if (!record.TradeRequested && !record.QuoteRequested && !record.MarketDepthRequested)
                    {
                        _dictDepthMarketData.Remove(altSymbol);
                        _dictAltSymbol2Instrument.Remove(altSymbol);
                        mdlog.Info("取消合约 {0} {1} {2}", altSymbol, record.Symbol, record.Exchange);
                        MdApi.MD_Unsubscribe(m_pMdApi, record.Symbol, record.Exchange);
                    }
                }
            }
        }

        private void EmitNewQuoteEvent(IFIXInstrument instrument, Quote quote)
        {
            if (this.MarketDataFilter != null)
            {
                quote = this.MarketDataFilter.FilterQuote(quote, instrument.Symbol);
            }

            if (quote != null)
            {
                if (NewQuote != null)
                {
                    NewQuote(this, new QuoteEventArgs(quote, instrument, this));
                }
                if (factory != null)
                {
                    factory.OnNewQuote(instrument, quote);
                }
            }
        }

        private void EmitNewTradeEvent(IFIXInstrument instrument, Trade trade)
        {
            if (this.MarketDataFilter != null)
            {
                trade = this.MarketDataFilter.FilterTrade(trade, instrument.Symbol);
            }

            if (trade != null)
            {
                if (NewTrade != null)
                {
                    NewTrade(this, new TradeEventArgs(trade, instrument, this));
                }
                if (factory != null)
                {
                    factory.OnNewTrade(instrument, trade);
                }
            }
        }

        private void EmitNewMarketDepth(IFIXInstrument instrument, MarketDepth marketDepth)
        {
            if (NewMarketDepth != null)
            {
                NewMarketDepth(this, new MarketDepthEventArgs(marketDepth, instrument, this));
            }
        }
        #endregion


        #region OpenQuant3接口的新方法
        public IMarketDataFilter MarketDataFilter { get; set; }
        #endregion
    }
}
