// Offline adapter to the pinned canonical FFX Hooks consumers. Never loads a game DLL.
#include "ElementPackAdmission.h"
#include "ElementMonsterProof.h"
#include "MonsterRewardSettings.h"
#include "SpiraAbilityCatalog.h"
#include "VanguardCatalog.h"
#include "AeonAscensionCore.h"
#include "ArcanaStore.h"
#include <fstream>
#include <iostream>
#include <iterator>
#include <stdexcept>
#ifdef _WIN32
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib,"bcrypt.lib")
#else
#include <openssl/sha.h>
#endif

static std::string read(const char* path,std::size_t maximum) {
    std::ifstream stream(path,std::ios::binary|std::ios::ate);
    if(!stream||stream.tellg()<0||static_cast<std::size_t>(stream.tellg())>maximum)throw std::runtime_error("Missing/oversized input");
    std::string bytes(static_cast<std::size_t>(stream.tellg()),'\0');stream.seekg(0);
    if(!bytes.empty()&&!stream.read(bytes.data(),static_cast<std::streamsize>(bytes.size())))throw std::runtime_error("Truncated input");
    return bytes;
}
static bool digest(const unsigned char* p,std::size_t n,std::array<unsigned char,32>& out) noexcept {
#ifdef _WIN32
    BCRYPT_ALG_HANDLE a=nullptr;BCRYPT_HASH_HANDLE h=nullptr;
    if(BCryptOpenAlgorithmProvider(&a,BCRYPT_SHA256_ALGORITHM,nullptr,0)<0)return false;
    bool ok=BCryptCreateHash(a,&h,nullptr,0,nullptr,0,0)>=0&&BCryptHashData(h,const_cast<unsigned char*>(p),static_cast<ULONG>(n),0)>=0&&BCryptFinishHash(h,out.data(),32,0)>=0;
    if(h)BCryptDestroyHash(h);BCryptCloseAlgorithmProvider(a,0);return ok;
#else
    return SHA256(p,n,out.data())!=nullptr;
#endif
}
int main(int argc,char** argv) {
    try {
        if(argc==2&&std::string(argv[1])=="identity") {std::cout<<"ffx.editor.contracts.v1 dda5cb45305448d761e5412f3b265e58213d8ce2\n";return 0;}
        if(argc<3)throw std::runtime_error("Usage: mode manifest [bank-path locale...] [monster-path...]");
        const std::string mode=argv[1],input=read(argv[2],1024*1024);
        if(mode=="workshop-state"||mode=="workshop-plan"||mode=="ascension") {
            if(ws_plan_abi()!=5)throw std::runtime_error("Workshop planning ABI mismatch");
            workshop::State state{};
            if(input.size()!=sizeof(state))throw std::runtime_error("Workshop state size mismatch");
            std::memcpy(&state,input.data(),sizeof(state));
            auto code=workshop::Validate(state);
            if(code!=workshop::Error::Ok)throw std::runtime_error(workshop::Message(code));
            if(mode=="workshop-plan") {
                if(argc!=5)throw std::runtime_error("Expected request/economy snapshots");
                const auto requestBytes=read(argv[3],sizeof(workshop::Request)),economyBytes=read(argv[4],sizeof(workshop::Economy));
                if(requestBytes.size()!=sizeof(workshop::Request)||economyBytes.size()!=sizeof(workshop::Economy))throw std::runtime_error("Transient ABI size mismatch");
                workshop::Request request{};workshop::Economy economy{};workshop::Plan plan{};
                std::memcpy(&request,requestBytes.data(),sizeof(request));std::memcpy(&economy,economyBytes.data(),sizeof(economy));
                code=static_cast<workshop::Error>(ws_plan_economy_v5(&state,&request,&economy,&plan));
                if(code!=workshop::Error::Ok)throw std::runtime_error(workshop::Message(code));
                // Intentionally do not serialize after/chosenAbility/costs/gilCost. Mode B
                // previews must not leak their winner, including through price differences.
            } else if(mode=="ascension") {
                if(argc!=5)throw std::runtime_error("Expected ledger/save identity");
                const auto ledgerBytes=read(argv[3],680),saveBytes=read(argv[4],32);
                if(ledgerBytes.size()!=680||saveBytes.size()!=32)throw std::runtime_error("Ascension wire size mismatch");
                FfxHooks::AeonAscension::Ledger ledger{};FfxHooks::AeonAscension::SaveId save{};
                std::memcpy(&ledger,ledgerBytes.data(),680);std::memcpy(save.data(),saveBytes.data(),32);
                if(!FfxHooks::AeonAscension::Validate(ledger,save,state))throw std::runtime_error("Foreign/invalid Ascension ownership");
            }
        } else if(mode=="abilities") {
            namespace S=FfxHooks::SpiraAbilities;namespace V=FfxHooks::Vanguard;namespace J=FfxHooks::ElementalDominion::Json;
            if(argc!=6)throw std::runtime_error("Expected mapping, name encoding and price table");
            J::Value mapping;J::Problem problem;const auto mappingText=read(argv[3],65535);
            if(!J::Parse(mappingText,mapping,problem)||mapping.type!=J::Type::Object||mapping.children.size()!=40)throw std::runtime_error("Invalid shared ability mapping");
            auto vanguard=V::DefaultMapping();auto spira=S::DefaultMapping();
            for(unsigned i=0;i<V::AbilityCount;++i){const auto* item=mapping.Find(V::Abilities[i].key);if(!item||item->type!=J::Type::Integer||item->integer<0||item->integer>4095)throw std::runtime_error("Missing Vanguard identity");vanguard[i]=static_cast<unsigned>(item->integer);}
            for(unsigned i=0;i<S::Count;++i){const auto* item=mapping.Find(S::Entries[i].key);if(!item||item->type!=J::Type::Integer||item->integer<0||item->integer>4095)throw std::runtime_error("Missing Spira identity");spira[i]=static_cast<unsigned>(item->integer);}
            const std::string encoding=argv[4];if(encoding!="latin"&&encoding!="numeric")throw std::runtime_error("Unknown identity encoding");
            const auto* bytes=reinterpret_cast<const unsigned char*>(input.data());unsigned last=0;std::size_t pool=0;
            if(!S::Table(bytes,input.size(),last,pool))throw std::runtime_error("Invalid native autoability bank");
            const auto price=read(argv[5],100000);const auto* rates=reinterpret_cast<const unsigned char*>(price.data());
            if(price.size()<20||S::Dword(rates)!=1||S::Word(rates+8)||S::Word(rates+10)<last||S::Word(rates+12)!=4||S::Dword(rates+16)!=20||
                S::Word(rates+14)!=(S::Word(rates+10)+1)*4||20u+S::Word(rates+14)>price.size())throw std::runtime_error("Missing native price coverage");
            auto admitted=S::Validate(bytes,input.size(),spira,vanguard.data(),V::AbilityCount,encoding=="numeric"?S::NameEncoding::NumericPlaceholder:S::NameEncoding::Latin,S::AllConsumers);
            for(const auto& binding:admitted)if(!binding.rowVerified)throw std::runtime_error("Spira row/payload/identity rejected");
            for(unsigned i=0;i<V::AbilityCount;++i){const auto id=vanguard[i];
                if(!FfxHooks::AutoAbilitySlots::Vanguard(id)||id>last)throw std::runtime_error("Reserved Vanguard row");
                for(unsigned j=0;j<i;++j)if(vanguard[j]==id)throw std::runtime_error("Vanguard collision");
                for(auto other:spira)if(other==id)throw std::runtime_error("Shared ability collision");
                const auto* row=bytes+20+id*108;for(unsigned at=16;at<108;++at)if(row[at])throw std::runtime_error("Vanguard payload is not neutral");
                std::string expected=encoding=="numeric"?std::to_string(V::Abilities[i].id):V::Abilities[i].label;
                const auto at=pool+S::Word(row);if(at>=input.size()||expected.size()>=input.size()-at)throw std::runtime_error("Vanguard name outside pool");
                for(std::size_t n=0;n<expected.size();++n)if(bytes[at+n]!=(encoding=="numeric"?static_cast<unsigned char>(expected[n]):S::Glyph(expected[n])))throw std::runtime_error("Vanguard identity mismatch");
                if(bytes[at+expected.size()])throw std::runtime_error("Vanguard name is not terminated");
            }
        } else if(mode=="arcana") {
            FfxHooks::Arcana::Record record;
            if(!FfxHooks::Arcana::Decode(reinterpret_cast<const unsigned char*>(input.data()),input.size(),record))throw std::runtime_error("Invalid Arcana record");
        } else if(mode=="rewards") {
            FfxHooks::MonsterRewards::RateTable rates{};
            if(!FfxHooks::MonsterRewards::ParseSettings(input,rates))throw std::runtime_error("Invalid native rewards table");
        } else if(mode=="elemental"||mode=="elemental-schema") {
            using namespace FfxHooks::ElementalDominion;
            Pack pack;PackProblem problem;
            if(!LoadPack(input,127,pack,problem)) {
                std::cerr<<"REJECTED field="<<problem.field<<" code="<<unsigned(problem.code)<<" json_offset="<<problem.json.offset<<"\n";return 2;
            }
            if(mode=="elemental-schema") {std::cout<<"PARSED RT0\n";return 0;}
            std::vector<std::string> data;data.reserve(pack.banks.size());
            std::vector<LoadedBank> loaded;loaded.reserve(pack.banks.size());int argument=3;
            for(const auto& bank:pack.banks) {
                if(argument+1>=argc)throw std::runtime_error("Missing bank input/locale");
                data.push_back(read(argv[argument++],bank.kind==BankKind::AutoAbility?65535:8*1024*1024));
                loaded.push_back({bank.kind,argv[argument++],reinterpret_cast<const unsigned char*>(data.back().data()),data.back().size(),nullptr});
            }
            AdmissionProblem admission;PackAdmission admitted;
            if(!admitted.Prepare(pack,loaded.data(),static_cast<unsigned>(loaded.size()),digest,admission)) {
                std::cerr<<"REJECTED admission="<<unsigned(admission.code)<<" bank="<<admission.bank<<" binding="<<admission.binding<<"\n";return 2;
            }
            for(const auto& profile:pack.profiles)if(profile.kind==ProfileKind::Monster) {
                if(argument>=argc)throw std::runtime_error("Missing full monster file");
                const auto monster=read(argv[argument++],8*1024*1024);MonsterProof proof;MonsterProofError error;
                if(!proof.Admit(profile,18,profile.id,monster.data(),reinterpret_cast<const unsigned char*>(monster.data()),monster.size(),digest,error)) {
                    std::cerr<<"REJECTED monster="<<profile.key<<" code="<<unsigned(error)<<"\n";return 2;
                }
            }
            if(argument!=argc)throw std::runtime_error("Unexpected inputs");
        } else throw std::runtime_error("Unknown validator mode");
        std::cout<<"ADMITTED RT0\n";return 0;
    } catch(const std::exception& e) {std::cerr<<"REJECTED "<<e.what()<<"\n";return 2;}
}
