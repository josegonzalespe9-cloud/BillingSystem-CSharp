using MicroRabbit.Banking.Domain.Interfaces;
using MicroRabbit.Banking.Application.Interfaces;
using MicroRabbit.Banking.Domain.Models;

namespace MicroRabbit.Banking.Application.Services
{
    public class AccountService : IAccountService
    {
        private readonly IAccountRepository _accRepository;

        public AccountService(IAccountRepository accRepository)
        {
            _accRepository = accRepository;
        }

        public IEnumerable<Account> GetAccounts()
        {
            return _accRepository.GetAccounts();
        }
    }
}
